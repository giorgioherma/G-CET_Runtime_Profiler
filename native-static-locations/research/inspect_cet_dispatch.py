"""Read-only attachment evidence for official CET 1.37.1 (not a hook installer).

Use the upstream release's exact asset digests and PE/PDB CodeView identity.
PDB addresses are section-relative; never assume an RVA from a source version.
No installed files, game process, or profiler payload are opened or modified.
"""
import argparse
import hashlib
import json
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


def check_pair(pe, pdb):
    require(pe.identity == pdb.identity, "PE/PDB GUID or age mismatch")


def inspect(release, symbols):
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
    return {
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
        "scope": "offline symbol discovery only; no detour, ABI or gameplay proof",
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("release", type=Path)
    parser.add_argument("symbols", type=Path)
    args = parser.parse_args()
    report = inspect(args.release, args.symbols)
    print(json.dumps(report, indent=2))
    # Missing public symbols are a research finding, not permission to attach.
    # Report exact unresolved targets; the later attachment gate must prove them.
    if report["missing_or_ambiguous"]:
        print("ATTACHMENT NOT PROVEN: unresolved or ambiguous public symbols")


if __name__ == "__main__":
    main()
