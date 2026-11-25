"""Read-only attachment evidence for official CET 1.37.1 (not a hook installer).

Use the upstream release's exact asset digests and PE/PDB CodeView identity.
PDB addresses are section-relative; never assume an RVA from a source version.
No installed files, game process, or profiler payload are opened or modified.
"""
import argparse
import hashlib
import json
import re
import subprocess
import tempfile
import struct
import uuid
import zipfile
from pathlib import Path

RELEASE = {
    "cet_1.37.1.zip": "1855017796a27f518199f5b7d7210ef1db7a5c5f0af468c68e04e6e666ad248c",
    "windows-latest-x64-release-pdb.zip": "1bb2ede3f80e8ac441bdbc24760dbcd2c8a04cfbe1ec68d75c217cc502928838",
}
SOURCE = "61bd6214f0f5f8748589c9e476538614a13908c0"
TARGETS = {
    "HookRunPureScriptFunction": "?HookRunPureScriptFunction@FunctionOverride@@",
    "HandleOverridenFunction": "?HandleOverridenFunction@FunctionOverride@@",
    "ExecuteChain": "?ExecuteChain@FunctionOverride@@",
    "Override": "?Override@FunctionOverride@@",
    "Clear": "?Clear@FunctionOverride@@",
    "ToLua": "?ToLua@Scripting@@",
}
MSF_MAGIC = b"Microsoft C/C++ MSF 7.00\r\n\x1aDS\x00\x00\x00"


def require(condition, reason):
    if not condition:
        raise ValueError(reason)


def unpack(fmt, data, off):
    require(0 <= off <= len(data) - struct.calcsize(fmt), "truncated record")
    return struct.unpack_from(fmt, data, off)


def take(data, off, size):
    require(0 <= off <= len(data) and 0 <= size <= len(data) - off,
            "range outside input")
    return data[off:off + size]


def sha(data):
    return hashlib.sha256(data).hexdigest()


def checked_archive(path, expected):
    require(sha(path.read_bytes()) == expected, f"release archive digest mismatch: {path.name}")
    return zipfile.ZipFile(path)


class PE:
    def __init__(self, data):
        self.data = data
        require(data[:2] == b"MZ", "not a PE")
        nt, = unpack("<I", data, 0x3c)
        require(take(data, nt, 4) == b"PE\0\0", "bad PE signature")
        machine, count, self.timestamp, _, _, optional_size, _ = unpack("<HHIIIHH", data, nt + 4)
        opt = nt + 24
        require(machine == 0x8664 and unpack("<H", data, opt)[0] == 0x20b,
                "CET is not AMD64 PE32+")
        require(optional_size >= 168 and unpack("<I", data, opt + 108)[0] > 6,
                "PE lacks debug data directory")
        self.image_size, = unpack("<I", data, opt + 56)
        self.exception_directory = unpack("<II", data, opt + 112 + 3 * 8)
        self.import_directory = unpack("<II", data, opt + 112 + 8)
        self.sections = []
        for i in range(count):
            off = opt + optional_size + 40 * i
            raw = take(data, off, 40)
            virtual_size, rva, size, pointer = unpack("<IIII", raw, 8)
            flags, = unpack("<I", raw, 36)
            self.sections.append((raw[:8].rstrip(b"\0").decode("ascii"),
                                  virtual_size, rva, size, pointer, flags))
        rva, size = unpack("<II", data, opt + 112 + 6 * 8)
        require(size > 0 and size % 28 == 0, "bad PE debug directory")
        directory = self.read_rva(rva, size)
        ids = set()
        for off in range(0, len(directory), 28):
            _, _, _, _, kind, length, address, pointer = unpack("<IIHHIIII", directory, off)
            if kind == 2 and length >= 24:
                cv = take(data, pointer, length)
                if cv[:4] == b"RSDS":
                    require(self.read_rva(address, length) == cv, "CodeView file/RVA mismatch")
                    ids.add((cv[4:20], unpack("<I", cv, 20)[0]))
        require(len(ids) == 1, "missing or ambiguous RSDS identity")
        self.identity = ids.pop()

    def read_rva(self, rva, size):
        for _, _, base, rawsize, pointer, _ in self.sections:
            if base <= rva and rva - base + size <= rawsize:
                return take(self.data, pointer + rva - base, size)
        raise ValueError("RVA not backed by PE section bytes")

    def cstring(self, rva):
        result = bytearray()
        for i in range(4096):
            char = self.read_rva(rva + i, 1)
            if char == b"\0":
                return result.decode("ascii")
            result.extend(char)
        raise ValueError("unterminated PE string")

    def imports(self):
        rva, size = self.import_directory
        require(rva and size >= 20, "missing import directory")
        result = {}
        for i in range(size // 20):
            original, stamp, forwarder, name, iat = unpack("<IIIII", self.read_rva(rva + i * 20, 20), 0)
            if not any((original, stamp, forwarder, name, iat)):
                return result
            require(name and iat, "invalid import descriptor")
            dll = self.cstring(name)
            for j in range(65536):
                value, = unpack("<Q", self.read_rva((original or iat) + j * 8, 8), 0)
                if not value:
                    break
                symbol = f"ordinal:{value & 0xffff}" if value >> 63 else self.cstring(value + 2)
                result[iat + j * 8] = f"{dll}!{symbol}"
            else:
                raise ValueError("unterminated import thunk array")
        raise ValueError("unterminated import descriptor array")

    def function_extent(self, start):
        rva, size = self.exception_directory
        require(size > 0 and size % 12 == 0, "bad x64 exception directory")
        ends = [end for begin, end, _ in struct.iter_unpack("<III", self.read_rva(rva, size))
                if begin == start and end > begin]
        require(len(ends) == 1, f"no unique x64 runtime function at {start:#x}")
        return start, ends[0]

    def function(self, segment, offset):
        require(1 <= segment <= len(self.sections), "invalid symbol section")
        name, virtual_size, rva, size, _, flags = self.sections[segment - 1]
        require(flags & 0x20000000 and offset < min(virtual_size or size, size),
                "symbol is not executable image code")
        address = rva + offset
        require(address < self.image_size, "symbol outside image")
        # Evidence only, not a verified detour span.
        sample = self.read_rva(address, min(16, size - offset))
        return {"section": name, "rva": f"0x{address:x}", "entry_bytes": sample.hex()}


class PDB:
    def __init__(self, data):
        self.data = data
        require(data.startswith(MSF_MAGIC), "not an MSF 7 PDB")
        block, _, blocks, directory_size, _, block_map = unpack("<6I", data, 32)
        require(block in (512, 1024, 2048, 4096, 8192) and blocks * block == len(data),
                "unsupported PDB block layout")
        self.block = block
        self.blocks = blocks
        n = self.count(directory_size)
        require(n * 4 <= block, "multi-block directory map is not supported")
        directory = self.join(unpack(f"<{n}I", self.page(block_map), 0), directory_size)
        streams, = unpack("<I", directory, 0)
        sizes = unpack(f"<{streams}I", directory, 4)
        off = 4 + 4 * streams
        self.streams = []
        for size in sizes:
            if size == 0xffffffff:
                self.streams.append(None)
                continue
            n = self.count(size)
            pages = unpack(f"<{n}I", directory, off)
            off += n * 4
            self.streams.append((pages, size))
        require(off <= len(directory), "truncated stream directory")
        info = self.stream(1)
        self.identity = (take(info, 12, 16), unpack("<I", info, 8)[0])

    def count(self, size):
        return (size + self.block - 1) // self.block

    def page(self, index):
        require(index < self.blocks, "PDB page outside file")
        return take(self.data, index * self.block, self.block)

    def join(self, pages, size):
        require(len(pages) == self.count(size), "PDB stream block count mismatch")
        return b"".join(self.page(x) for x in pages)[:size]

    def stream(self, index):
        require(0 <= index < len(self.streams) and self.streams[index] is not None,
                f"missing PDB stream {index}")
        return self.join(*self.streams[index])

    def type_records(self):
        tpi = self.stream(2)
        header, first, last, size = unpack("<IIII", tpi, 4)
        data = take(tpi, header, size)
        records = {}
        pos = 0
        for index in range(first, last):
            length, kind = unpack("<HH", data, pos)
            require(length >= 2, "invalid TPI record length")
            records[index] = (kind, take(data, pos + 4, length - 2))
            pos += length + 2
        require(pos == len(data), "TPI record range mismatch")
        return records

    def public_records(self, pe):
        dbi = self.stream(3)
        require(unpack("<I", dbi, 8)[0] == self.identity[1], "DBI age mismatch")
        sym_stream, = unpack("<H", dbi, 20)
        # Reject OMAP remapping rather than silently treating source offsets
        # as linked image RVAs. Compare linked section table when present.
        sizes = [unpack("<i", dbi, i)[0] for i in (24, 28, 32, 36, 40, 52)]
        debug_size, = unpack("<i", dbi, 48)
        require(all(x >= 0 for x in sizes) and debug_size >= 0 and debug_size % 2 == 0,
                "bad DBI substream sizes")
        debug = take(dbi, 64 + sum(sizes), debug_size)
        debug_ids = unpack(f"<{debug_size // 2}H", debug, 0)
        require(all(i >= len(debug_ids) or debug_ids[i] == 0xffff for i in (3, 4)),
                "PDB contains OMAP; direct section mapping is unproven")
        if len(debug_ids) > 5 and debug_ids[5] != 0xffff:
            sections = self.stream(debug_ids[5])
            require(len(sections) == 40 * len(pe.sections), "PDB/PE section count mismatch")
            for i, (_, vsize, rva, rawsize, _, flags) in enumerate(pe.sections):
                off = 40 * i
                require(unpack("<III", sections, off + 8) == (vsize, rva, rawsize)
                        and unpack("<I", sections, off + 36)[0] == flags,
                        "PDB/PE section layout mismatch")
        records = self.stream(sym_stream)
        off = 0
        while off < len(records):
            length, kind = unpack("<HH", records, off)
            require(length >= 2, "invalid CodeView record length")
            payload = take(records, off + 4, length - 2)
            if kind == 0x110e:  # S_PUB32: flags, offset, segment, NUL name
                flags, address, segment = unpack("<IIH", payload, 0)
                name, sep, _ = payload[10:].partition(b"\0")
                require(sep, "unterminated public symbol name")
                if flags & 2:  # function, not data
                    yield name.decode("utf-8", errors="replace"), segment, address
            off += length + 2


def numeric(data, pos):
    leaf, = unpack("<H", data, pos)
    if leaf < 0x8000:
        return leaf, pos + 2
    formats = {0x8000: "<b", 0x8001: "<h", 0x8002: "<H",
               0x8003: "<i", 0x8004: "<I", 0x8009: "<q", 0x800a: "<Q"}
    require(leaf in formats, f"unsupported numeric leaf {leaf:#x}")
    fmt = formats[leaf]
    return unpack(fmt, data, pos + 2)[0], pos + 2 + struct.calcsize(fmt)


class TypeLayouts:
    """Decode the actual PDB class/field records, never a guessed STL ABI."""
    def __init__(self, pdb, pdb_path, pdbutil):
        self.types = pdb.type_records()
        self.pdb_path, self.pdbutil = pdb_path, pdbutil
        self.classes = {}
        self.by_name = {}
        self.cache = {}
        for index, (kind, data) in self.types.items():
            if kind not in (0x1504, 0x1505):
                continue
            _, props, fields, _, _ = unpack("<HHIII", data, 0)
            size, off = numeric(data, 16)
            name = data[off:].split(b"\0", 1)[0].decode("utf-8", errors="replace")
            self.classes[index] = (name, fields, size)
            if not (props & 0x80) and fields:
                self.by_name.setdefault(name, []).append(index)

    def full_index(self, index):
        require(index in self.classes, f"type {index:#x} is not a class")
        name, _, _ = self.classes[index]
        candidates = self.by_name.get(name, [])
        require(len(candidates) == 1, f"missing or ambiguous full PDB type: {name}: {candidates}")
        return candidates[0]

    def named(self, name):
        candidates = self.by_name.get(name, [])
        require(len(candidates) == 1, f"missing or ambiguous full PDB type: {name}: {candidates}")
        return self.layout(candidates[0])

    def layout(self, index):
        index = self.full_index(index)
        if index in self.cache:
            return self.cache[index]
        name, fields, size = self.classes[index]
        seen, members, queue = set(), {}, [fields]
        while queue:
            field_index = queue.pop()
            require(field_index not in seen, "cyclic field-list continuation")
            seen.add(field_index)
            require(self.types[field_index][0] == 0x1203, "not a field list")
            output = subprocess.check_output(
                [self.pdbutil, "dump", "-types", f"-type-index=0x{field_index:x}", str(self.pdb_path)],
                text=True, stderr=subprocess.STDOUT)
            for match in re.finditer(r"\[name = \x60([^\x60]+)\x60, Type = ([^,]+), offset = (0x[0-9a-fA-F]+|\d+), attrs =", output):
                member, member_type, off = match.groups()
                offset = int(off, 16 if off.startswith("0x") else 10)
                require(0 <= offset < size, f"{name}.{member} outside class")
                require(member not in members, f"duplicate field {name}.{member}")
                members[member] = {"offset": offset, "type": member_type}
            queue.extend(int(x, 16) for x in re.findall(r"continuation = (0x[0-9a-fA-F]+)", output))
        result = {"name": name, "size": size, "type_index": hex(index), "fields": members}
        self.cache[index] = result
        return result

    @staticmethod
    def member_index(member):
        m = re.match(r"0x([0-9a-fA-F]+)\b", member["type"])
        require(m is not None, f"unresolved member type {member}")
        return int(m.group(1), 16)

    def field(self, index, path):
        root = self.layout(index)
        offset = 0
        layout = root
        for step, field in enumerate(path):
            require(field in layout["fields"], f"missing {layout['name']}.{field}: {layout['fields']}")
            member = layout["fields"][field]
            offset += member["offset"]
            index = self.member_index(member)
            if step + 1 != len(path):
                child = self.layout(index)
                require(member["offset"] + child["size"] <= layout["size"], "embedded member exceeds class size")
                layout = child
        require(offset < root["size"], "member path outside root")
        return offset, index

    def pointer_target(self, index):
        kind, data = self.types[index]
        require(kind == 0x1002, f"{index:#x} is not LF_POINTER")
        target, attrs = unpack("<II", data, 0)
        require((attrs & 0x1f) == 0x0c and ((attrs >> 5) & 7) == 0
                and ((attrs >> 13) & 0x3f) == 8, "not an ordinary x64 pointer")
        return target


def class_layouts(pdb, pdb_path, pdbutil):
    targets = {
        "FunctionOverride": ("m_functions", "m_trampolines", "m_pScripting", "m_lock"),
        "FunctionOverride::CallChain": ("Trampoline", "pScripting", "Before", "After", "Overrides", "IsEmpty", "CollectGarbage"),
        "FunctionOverride::Context": ("ScriptFunction", "Environment", "Forward"),
    }
    types = TypeLayouts(pdb, pdb_path, pdbutil)
    result = {}
    for name, expected in targets.items():
        layout = types.named(name)
        missing = set(expected) - layout["fields"].keys()
        require(not missing, f"missing PDB members: {name}: {sorted(missing)}")
        result[name] = layout

    # A narrow future gate needs to prove exactly one managed Before
    # callback and zero After/Overrides without scanning or mutating CET's
    # vectors. Derive begin/end/capacity and element ownership from the
    # embedded concrete types in the matched release PDB.
    chain = result["FunctionOverride::CallChain"]
    context = result["FunctionOverride::Context"]
    vectors = [chain["fields"][name] for name in ("Before", "After", "Overrides")]
    indices = [types.full_index(types.member_index(v)) for v in vectors]
    require(len(set(indices)) == 1, "callback vector types differ")
    vi = indices[0]
    vector = types.layout(vi)
    pointers = {}
    element_types = []
    for name in ("_Myfirst", "_Mylast", "_Myend"):
        offset, ti = types.field(vi, ("_Mypair", "_Myval2", name))
        require(offset % 8 == 0 and offset + 8 <= vector["size"], "invalid vector pointer position")
        pointers[name] = offset
        element_types.append(types.full_index(types.pointer_target(ti)))
    require(len(set(pointers.values())) == 3 and len(set(element_types)) == 1,
            "inconsistent callback vector storage")
    ei = element_types[0]
    element = types.layout(ei)
    ptr_offset, ti = types.field(ei, ("_Mypair", "_Myval2"))
    require(ptr_offset % 8 == 0 and ptr_offset + 8 <= element["size"], "invalid Context owner pointer position")
    require(types.full_index(types.pointer_target(ti)) == int(context["type_index"], 16),
            "callback vector element does not own the exact Context type")
    for v in vectors:
        require(v["offset"] + vector["size"] <= chain["size"], "callback vector exceeds chain")
    result["CallbackVectorMembership"] = {
        "vector_type": vector["name"], "vector_size": vector["size"],
        "pointer_offsets": pointers, "element_type": element["name"],
        "element_stride": element["size"], "context_pointer_offset": ptr_offset,
        "chain_relative": {
            name: {key: v["offset"] + value for key, value in pointers.items()}
            for name, v in zip(("Before", "After", "Overrides"), vectors)
        },
        "scope": "read-only compiled membership layout; runtime identity/lifetime not proven",
    }
    return result


def branch_proof(pe, matches, layouts):
    from capstone import Cs, CS_ARCH_X86, CS_MODE_64, CS_GRP_JUMP, CS_GRP_RET, CS_GRP_CALL
    from capstone.x86_const import X86_OP_IMM, X86_OP_MEM
    cs = Cs(CS_ARCH_X86, CS_MODE_64)
    cs.detail = True
    off = layouts["FunctionOverride::CallChain"]["fields"]["IsEmpty"]["offset"]
    result = {}
    addresses = {name: int(values[0]["rva"], 16) for name, values in matches.items()
                 if len(values) == 1}
    require(all(x in addresses for x in ("ToLua", "ExecuteChain")), "dispatch symbols not unique")
    for name in ("HookRunPureScriptFunction", "HandleOverridenFunction"):
        start, end = pe.function_extent(addresses[name])
        instructions = list(cs.disasm(pe.read_rva(start, end - start), start))
        by_addr = {i.address: i for i in instructions}
        require(instructions and instructions[-1].address + instructions[-1].size == end,
                f"partial decode: {name}")
        candidates = []
        for i in instructions:
            if i.mnemonic != "cmp" or len(i.operands) != 2:
                continue
            lhs, rhs = i.operands
            if not (lhs.type == X86_OP_MEM and lhs.size == 1 and lhs.mem.disp == off
                    and rhs.type == X86_OP_IMM and rhs.imm == 0):
                continue
            branch = by_addr.get(i.address + i.size)
            if not branch or branch.mnemonic not in ("je", "jne") or branch.operands[0].type != X86_OP_IMM:
                continue
            target = branch.operands[0].imm
            fall = branch.address + branch.size

            def walk(entry):
                seen, pending, calls, indirect, exits = set(), [entry], set(), set(), set()
                while pending:
                    address = pending.pop()
                    if address in seen:
                        continue
                    seen.add(address)
                    ins = by_addr.get(address)
                    if ins is None:
                        exits.add(hex(address))
                        continue
                    next_addr = address + ins.size
                    if ins.group(CS_GRP_RET):
                        exits.add(hex(address))
                        continue
                    if ins.group(CS_GRP_CALL):
                        if ins.operands and ins.operands[0].type == X86_OP_IMM:
                            calls.add(ins.operands[0].imm)
                        else:
                            indirect.add(hex(address))
                    if ins.group(CS_GRP_JUMP):
                        if ins.operands and ins.operands[0].type == X86_OP_IMM:
                            pending.append(ins.operands[0].imm)
                        else:
                            indirect.add(hex(address))
                        if ins.mnemonic == "jmp":
                            continue
                    pending.append(next_addr)
                return {"direct_targets": [key for key in ("ToLua", "ExecuteChain") if addresses[key] in calls],
                        "indirect_sites": len(indirect), "exits": sorted(exits)}

            active_edge, empty_edge = (target, fall) if branch.mnemonic == "je" else (fall, target)
            active, empty = walk(active_edge), walk(empty_edge)
            if set(active["direct_targets"]) == {"ToLua", "ExecuteChain"} and not empty["direct_targets"]:
                candidates.append({"compare_rva": hex(i.address), "compare": f"{i.mnemonic} {i.op_str}",
                                   "branch_rva": hex(branch.address), "branch": f"{branch.mnemonic} {branch.op_str}",
                                   "active_edge": hex(active_edge), "empty_edge": hex(empty_edge),
                                   "active": active, "empty": empty})
        require(len(candidates) == 1, f"{name}: expected one pre-marshalling branch, found {len(candidates)}")
        result[name] = {"extent": [hex(start), hex(end)], **candidates[0]}
        guard = int(candidates[0]["compare_rva"], 16)
        active = int(candidates[0]["active_edge"], 16)
        empty = int(candidates[0]["empty_edge"], 16)
        # A branch-island design must not assume arbitrary volatile registers
        # are dead at an interior point. Show both successors and the exact
        # surrounding instructions with explicit register access.
        def describe(i):
            reads, writes = i.regs_access()
            return {"rva": hex(i.address), "bytes": i.bytes.hex(),
                    "instruction": f"{i.mnemonic} {i.op_str}",
                    "reads": [i.reg_name(r) for r in reads],
                    "writes": [i.reg_name(r) for r in writes]}
        # RAX is the ONLY scratch register allowed by the proposed stackless
        # gate. Walk each original successor until a full RAX write or a
        # Win64 call (RAX not an argument register). Refuse any prior read,
        # indirect/unknown control flow, return, or undecoded path.
        aliases = {"rax", "eax", "ax", "al", "ah"}
        full = {"rax", "eax"}
        def prove_rax_dead(entry):
            pending, seen, killers = [entry], set(), []
            while pending:
                at = pending.pop()
                if at in seen:
                    continue
                seen.add(at)
                ins = by_addr.get(at)
                require(ins is not None, f"{name}: unknown RAX path at {at:#x}")
                reads, writes = ins.regs_access()
                require(not aliases.intersection(ins.reg_name(r) for r in reads),
                        f"{name}: RAX live at {at:#x}: {ins.mnemonic} {ins.op_str}")
                require(not ins.group(CS_GRP_RET), f"{name}: RAX reaches return")
                if full.intersection(ins.reg_name(r) for r in writes) or ins.group(CS_GRP_CALL):
                    killers.append(hex(at))
                    continue
                if ins.group(CS_GRP_JUMP):
                    require(ins.operands and ins.operands[0].type == X86_OP_IMM,
                            f"{name}: unknown branch before RAX kill")
                    pending.append(ins.operands[0].imm)
                    if ins.mnemonic == "jmp":
                        continue
                pending.append(ins.address + ins.size)
            require(killers, f"{name}: no RAX kill proven")
            return sorted(killers)
        require(by_addr[empty].mnemonic == "cmp" and
                by_addr[empty].operands[0].type == X86_OP_MEM and
                by_addr[empty].operands[0].mem.disp == layouts["FunctionOverride::CallChain"]["fields"]["CollectGarbage"]["offset"],
                f"{name}: empty successor does not immediately reset flags at GC check")
        require(not any(ins.group(CS_GRP_JUMP) and ins.operands and
                        ins.operands[0].type == X86_OP_IMM and
                        guard < ins.operands[0].imm < active
                        for ins in instructions), f"{name}: incoming branch inside guard")
        result[name]["rax_dead_before_use"] = {"active": prove_rax_dead(active), "empty": prove_rax_dead(empty)}
        result[name]["guard_bytes"] = pe.read_rva(guard, active - guard).hex()
        result[name]["guard_context"] = [describe(i) for i in instructions
                                         if guard - 48 <= i.address < active + 64]
        result[name]["empty_context"] = [describe(i) for i in instructions
                                         if empty <= i.address < empty + 96]
        directory_rva, directory_size = pe.exception_directory
        records = [row for row in struct.iter_unpack("<III", pe.read_rva(directory_rva, directory_size))
                   if row[0] == start and row[1] == end]
        require(len(records) == 1, f"no unique unwind record for {name}")
        unwind = records[0][2]
        header = pe.read_rva(unwind, 4)
        count = header[2]
        result[name]["unwind"] = {"rva": hex(unwind), "version": header[0] & 7,
                                  "flags": header[0] >> 3, "prologue_size": header[1],
                                  "slot_count": count, "frame": header[3],
                                  "header_and_slots": pe.read_rva(unwind, 4 + ((count + 1) & ~1) * 2).hex()}

    return result


def personality_proof(pe, matches, pdb):
    from capstone import Cs, CS_ARCH_X86, CS_MODE_64, CS_GRP_CALL, CS_GRP_JUMP
    from capstone.x86_const import X86_OP_MEM, X86_OP_IMM, X86_REG_RIP
    cs = Cs(CS_ARCH_X86, CS_MODE_64)
    cs.detail = True
    imports = pe.imports()
    records = list(struct.iter_unpack("<III", pe.read_rva(*pe.exception_directory)))
    by_start = {begin: (end, unwind) for begin, end, unwind in records}
    symbols = {}
    for name, section, offset in pdb.public_records(pe):
        if 1 <= section <= len(pe.sections):
            symbols.setdefault(pe.sections[section-1][2] + offset, []).append(name)

    def trace(start, depth=0, seen=None):
        seen = set() if seen is None else seen
        if start in seen or depth > 3:
            return {"rva": hex(start), "limited": True}
        seen.add(start)
        end = by_start.get(start, (start + 16, 0))[0]
        size = min(end - start, 512)
        result = {"rva": hex(start), "symbols": symbols.get(start, []), "edges": []}
        for ins in cs.disasm(pe.read_rva(start, size), start):
            if not (ins.group(CS_GRP_CALL) or ins.group(CS_GRP_JUMP)) or not ins.operands:
                continue
            op = ins.operands[0]
            if op.type == X86_OP_MEM and op.mem.base == X86_REG_RIP:
                target = ins.address + ins.size + op.mem.disp
                if target in imports:
                    result["edges"].append({"at": hex(ins.address), "import": imports[target]})
            elif op.type == X86_OP_IMM and (ins.group(CS_GRP_CALL) or ins.mnemonic == "jmp"):
                target = op.imm
                if start <= target < end:
                    continue
                try:
                    result["edges"].append({"at": hex(ins.address), "target": trace(target, depth+1, seen)})
                except ValueError:
                    result["edges"].append({"at": hex(ins.address), "unresolved_rva": hex(target)})
        return result

    result = {}
    for name in ("HookRunPureScriptFunction", "HandleOverridenFunction", "ExecuteChain", "Clear"):
        start = int(matches[name][0]["rva"], 16)
        require(start in by_start, f"missing unwind record: {name}")
        end, unwind = by_start[start]
        header = pe.read_rva(unwind, 4)
        flags = header[0] >> 3
        item = {"function_rva": hex(start), "unwind_rva": hex(unwind), "flags": flags}
        if flags & 3 and not flags & 4:
            tail = unwind + 4 + ((header[2]+1)&~1)*2
            handler, = unpack("<I", pe.read_rva(tail, 4), 0)
            item.update(handler_rva=hex(handler), handler_data_rva=hex(tail+4),
                        handler_data_prefix=pe.read_rva(tail+4, 16).hex(),
                        handler=trace(handler))
        result[name] = item
    return {"exception_imports": sorted(v for v in imports.values()
        if any(k in v.lower() for k in ("handler", "cxxthrow", "unwind"))), "functions": result}


def check_pair(pe, pdb):
    require(pe.identity == pdb.identity, "PE/PDB GUID or age mismatch")


def inspect(release, symbols, pdbutil=None):
    with checked_archive(release, RELEASE["cet_1.37.1.zip"]) as z:
        names = [n for n in z.namelist()
                 if Path(n.replace("\\", "/")).name.lower() == "cyber_engine_tweaks.asi"]
        require(len(names) == 1, "official CET ASI missing or ambiguous")
        image = z.read(names[0])
    pe = PE(image)
    with checked_archive(symbols, RELEASE["windows-latest-x64-release-pdb.zip"]) as z:
        candidates = [n for n in z.namelist() if n.lower().endswith(".pdb")]
        matching = []
        for name in candidates:
            pdb = PDB(z.read(name))
            if pdb.identity == pe.identity:
                matching.append((name, pdb))
        require(len(matching) == 1, "no unique official PDB matches actual CET ASI")
        pdb_name, pdb = matching[0]
    check_pair(pe, pdb)
    # Negative control against the actual release identity, no new files.
    wrong = object.__new__(PDB)
    wrong.identity = (pdb.identity[0], pdb.identity[1] + 1)
    try:
        check_pair(pe, wrong)
    except ValueError:
        pass
    else:
        raise AssertionError("mismatched PDB was accepted")
    matches = {target: [] for target in TARGETS}
    for name, section, offset in pdb.public_records(pe):
        for target, prefix in TARGETS.items():
            if name.startswith(prefix):
                evidence = pe.function(section, offset)
                evidence["symbol"] = name
                if evidence not in matches[target]:
                    matches[target].append(evidence)
    layouts, branches, personalities = None, None, None
    if pdbutil is not None:
        require(all(len(v) == 1 for v in matches.values()), "unresolved dispatcher symbols")
        with tempfile.TemporaryDirectory() as tmp:
            pdb_path = Path(tmp) / "verified.pdb"
            pdb_path.write_bytes(pdb.data)
            layouts = class_layouts(pdb, pdb_path, pdbutil)
        print("VERIFIED COMPILED LAYOUTS:", json.dumps(layouts), flush=True)
        branches = branch_proof(pe, matches, layouts)
        personalities = personality_proof(pe, matches, pdb)
        print("VERIFIED EXCEPTION PERSONALITIES:", json.dumps(personalities), flush=True)
        field = layouts["FunctionOverride::CallChain"]["fields"]["IsEmpty"]
        original_offset = field["offset"]
        field["offset"] = layouts["FunctionOverride::CallChain"]["size"] + 4096
        try:
            branch_proof(pe, matches, layouts)
        except ValueError:
            pass
        else:
            raise AssertionError("wrong CallChain member offset accepted")
        finally:
            field["offset"] = original_offset
    return {
        "layouts": layouts,
        "pre_marshalling_branches": branches,
        "exception_personalities": personalities,
        "wrong_offset_negative_test": "PASS" if pdbutil is not None else "NOT_RUN",
        "source_commit": SOURCE,
        "asi_sha256": sha(image),
        "pdb_sha256": sha(pdb.data),
        "pdb_name": pdb_name,
        "codeview_guid": str(uuid.UUID(bytes_le=pe.identity[0])),
        "codeview_age": pe.identity[1],
        "image_size": pe.image_size,
        "negative_identity_test": "PASS",
        "symbols": matches,
        "missing_or_ambiguous": [k for k, v in matches.items() if len(v) != 1],
        "scope": "offline symbol/layout/control-flow evidence; no detour, live ABI or gameplay proof",
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("release", type=Path)
    parser.add_argument("symbols", type=Path)
    parser.add_argument("--pdbutil", help="require compiled layout and control-flow proof using LLVM and Capstone")
    args = parser.parse_args()
    report = inspect(args.release, args.symbols, args.pdbutil)
    print(json.dumps(report, indent=2))
    # Missing public symbols are a research finding, not permission to attach.
    # Report exact unresolved targets; the later attachment gate must prove them.
    if report["missing_or_ambiguous"]:
        print("ATTACHMENT NOT PROVEN: unresolved or ambiguous public symbols")


if __name__ == "__main__":
    main()
