"""Build a separate 8400 x 2400 empty world from a Terraria v279 save.

Preserves the template's progression and mod difficulty data. Never overwrites
the template or an existing output. No game process or player save is changed.
"""
from __future__ import annotations

import argparse
import datetime as dt
import gzip
import hashlib
import io
import json
import secrets
import struct
import uuid
from pathlib import Path


class Reader:
    def __init__(self, data: bytes):
        self.stream = io.BytesIO(data)

    def read(self, n: int) -> bytes:
        data = self.stream.read(n)
        if len(data) != n:
            raise ValueError("Unexpected end of save")
        return data

    def unpack(self, fmt: str):
        return struct.unpack(fmt, self.read(struct.calcsize(fmt)))[0]

    def string(self) -> str:
        length = shift = 0
        while True:
            b = self.unpack("B")
            length |= (b & 127) << shift
            if b < 128:
                return self.read(length).decode("utf-8")
            shift += 7
            if shift > 28:
                raise ValueError("Invalid .NET string length")


def string(value: str) -> bytes:
    encoded = value.encode("utf-8")
    n = len(encoded)
    prefix = bytearray()
    while n >= 128:
        prefix.append((n & 127) | 128)
        n >>= 7
    prefix.append(n)
    return bytes(prefix) + encoded


def nbt_string(r: Reader) -> str:
    return r.read(r.unpack(">H")).decode("utf-8")


def nbt_payload(r: Reader, kind: int):
    if kind in (1, 2, 3, 4, 5, 6):
        return r.unpack({1: ">b", 2: ">h", 3: ">i", 4: ">q", 5: ">f", 6: ">d"}[kind])
    if kind in (7, 11, 12):
        count = r.unpack(">i")
        if count < 0:
            raise ValueError("Negative NBT array length")
        return r.read(count * {7: 1, 11: 4, 12: 8}[kind])
    if kind == 8:
        return nbt_string(r)
    if kind == 9:
        element = r.unpack("B")
        count = r.unpack(">i")
        if count < 0:
            raise ValueError("Negative NBT list length")
        return element, [nbt_payload(r, element) for _ in range(count)]
    if kind == 10:
        result = {}
        while (child := r.unpack("B")) != 0:
            name = nbt_string(r)
            if name in result:
                raise ValueError("Duplicate NBT key")
            result[name] = (child, nbt_payload(r, child))
        return result
    raise ValueError(f"Unsupported NBT tag {kind}")


def nbt_write(kind: int, value) -> bytes:
    if kind in (1, 2, 3, 4, 5, 6):
        return struct.pack({1: ">b", 2: ">h", 3: ">i", 4: ">q", 5: ">f", 6: ">d"}[kind], value)
    if kind in (7, 11, 12):
        return struct.pack(">i", len(value) // {7: 1, 11: 4, 12: 8}[kind]) + value
    if kind == 8:
        data = value.encode("utf-8")
        return struct.pack(">H", len(data)) + data
    if kind == 9:
        child, elements = value
        return struct.pack(">Bi", child, len(elements)) + b"".join(nbt_write(child, x) for x in elements)
    if kind == 10:
        return b"".join(bytes([child]) + nbt_write(8, name) + nbt_write(child, payload)
                        for name, (child, payload) in value.items()) + b"\x00"
    raise ValueError(f"Unsupported NBT tag {kind}")


def read_twld(path: Path):
    raw = gzip.decompress(path.read_bytes())
    r = Reader(raw)
    if r.read(3) != b"\x0a\x00\x00":
        raise ValueError("Unexpected tModLoader NBT root")
    root = nbt_payload(r, 10)
    if r.stream.tell() != len(raw) or b"\x0a\x00\x00" + nbt_write(10, root) != raw:
        raise ValueError("NBT round-trip mismatch")
    return root


def summarize_nbt(node):
    kind, value = node
    if kind in (7, 11, 12):
        return {"type": kind, "bytes": len(value), "nonzero": any(value)}
    if kind == 9:
        child, elements = value
        return [summarize_nbt((child, x)) for x in elements] if len(elements) < 25 else {"count": len(elements)}
    if kind == 10:
        return {k: summarize_nbt(v) for k, v in value.items()}
    return value


def file_sections(data: bytes):
    if struct.unpack_from("<i", data)[0] != 279 or data[4:12] != b"relogic\x02":
        raise ValueError("Only the installed Terraria v279 world format is supported")
    count = struct.unpack_from("<h", data, 24)[0]
    if count != 11:
        raise ValueError("Unexpected world section count")
    positions = list(struct.unpack_from("<11i", data, 26))
    if positions != sorted(positions) or not 70 <= positions[0] < positions[-1] < len(data):
        raise ValueError("Invalid section pointers")
    return positions, [data[a:b] for a, b in zip(positions, positions[1:] + [len(data)])]


def read_header(data: bytes):
    r = Reader(data)
    result = {"name": r.string(), "seed": r.string(), "generator": r.unpack("<Q"),
              "uuid": str(uuid.UUID(bytes_le=r.read(16)))}
    for key in ("id", "left", "right", "top", "bottom", "height", "width", "game_mode"):
        result[key] = r.unpack("<i")
    result["special_seeds"] = list(r.read(8))
    result["creation_time"] = r.unpack("<q")
    result["moon"] = r.unpack("B")
    result["landscape"] = [r.unpack("<i") for _ in range(17)]
    result["spawn_x"] = r.unpack("<i")
    result["spawn_y"] = r.unpack("<i")
    result["surface"] = r.unpack("<d")
    result["rock_layer"] = r.unpack("<d")
    result["tail_offset"] = r.stream.tell()
    return result


def build_world(template: bytes, name: str, surface: int = 2200, spawn_y: int = 1100):
    positions, sections = file_sections(template)
    original = read_header(sections[0])
    if original["game_mode"] != 2:
        raise ValueError("Use the existing Master/Masochist test-world template")
    if any(original["special_seeds"]):
        raise ValueError("Special-seed templates are not supported")
    width, height = 8400, 2400
    if not 100 < spawn_y < surface - 150 < height - 100:
        raise ValueError("Spawn and surface must leave usable surface airspace")
    world_id = secrets.randbelow(2**31 - 1) + 1
    world_uuid = uuid.uuid4()
    ticks = int((dt.datetime.now(dt.timezone.utc) - dt.datetime(1, 1, 1, tzinfo=dt.timezone.utc)).total_seconds() * 10_000_000)
    landscape = original["landscape"].copy()
    for index in (0, 1, 2, 7, 8, 9):
        landscape[index] = min(width - 1, round(landscape[index] * width / original["width"]))
    header = (string(name) + string("mutant-large-empty") + struct.pack("<Q", original["generator"])
              + world_uuid.bytes_le + struct.pack("<8i", world_id, 0, width * 16, 0, height * 16, height, width, 2)
              + bytes(8) + struct.pack("<qB", ticks | (1 << 62), original["moon"])
              + struct.pack("<17i", *landscape) + struct.pack("<iidd", width // 2, spawn_y, surface, surface + 50)
              + sections[0][original["tail_offset"]:])
    # A zero-flags tile with 16-bit vertical RLE expands to exactly 2400 empty
    # tiles. No blocks, walls, liquids, wires, paint, slopes or hidden tiles.
    tiles = (b"\x80" + struct.pack("<h", height - 1)) * width
    sections[0], sections[1] = header, tiles
    # Empty object sections use the installed engine's serialization layout.
    sections[2] = struct.pack("<hh", 0, 40)  # chests, slots per chest
    sections[3] = struct.pack("<h", 0)       # signs
    sections[4] = struct.pack("<iBB", 0, 0, 0)  # shimmered NPCs, town NPCs, saved NPCs
    for index in (5, 6, 7):
        sections[index] = struct.pack("<i", 0)  # tile entities, pressure plates, town rooms
    sections[10] = b"\x01" + string(name) + struct.pack("<i", world_id)
    prefix = bytearray(template[:positions[0]])
    struct.pack_into("<I", prefix, 12, 1)  # new file revision
    struct.pack_into("<Q", prefix, 16, 0)  # not a favorite
    new_positions = []
    cursor = len(prefix)
    for section in sections:
        new_positions.append(cursor)
        cursor += len(section)
    struct.pack_into("<11i", prefix, 26, *new_positions)
    data = bytes(prefix) + b"".join(sections)
    new_header = read_header(header)
    return data, original, new_header


def build_twld(root):
    # Missing tile-data keys explicitly mean no mod tiles/walls in TileIO.LoadData.
    # Do not retain the template's arrays, whose dimensions belong to the small map.
    root["tiles"] = (10, {})
    root["containers"] = (10, {})
    for key in ("chests", "npcs", "tileEntities", "townManager"):
        root[key] = (9, (10, []))
    return gzip.compress(b"\x0a\x00\x00" + nbt_write(10, root), mtime=0)


def verify_empty(data: bytes, expected_name: str):
    positions, sections = file_sections(data)
    header = read_header(sections[0])
    assert header["name"] == expected_name
    assert (header["width"], header["height"], header["game_mode"]) == (8400, 2400, 2)
    r = Reader(sections[1])
    for _ in range(header["width"]):
        total = 0
        while total < header["height"]:
            flags = r.unpack("B")
            assert flags & 63 == 0, "Nonempty tile flags"
            repeat = flags >> 6
            count = 1 + (r.unpack("B") if repeat == 1 else r.unpack("<h") if repeat >= 2 else 0)
            assert count > 0
            total += count
        assert total == header["height"]
    assert r.stream.tell() == len(sections[1])
    footer = Reader(sections[10])
    assert footer.unpack("B") == 1 and footer.string() == expected_name and footer.unpack("<i") == header["id"]
    assert footer.stream.tell() == len(sections[10])
    return header


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--template", type=Path, required=True)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--name", default="Mutant Empty Arena")
    parser.add_argument("--inspect", action="store_true")
    args = parser.parse_args()
    vanilla = args.template.read_bytes()
    _, sections = file_sections(vanilla)
    mod_root = read_twld(args.template.with_suffix(".twld"))
    if args.inspect:
        print(json.dumps({"header": read_header(sections[0]), "mod_data": summarize_nbt((10, mod_root))}, ensure_ascii=False, indent=2))
        return
    if not args.output or args.output.suffix.lower() != ".wld":
        parser.error("--output must name a new .wld file")
    outputs = [args.output, args.output.with_suffix(".twld"), args.output.with_suffix(".manifest.json")]
    if any(p.exists() for p in outputs):
        raise FileExistsError("Refusing to overwrite an existing world or manifest")
    world, original, header = build_world(vanilla, args.name)
    verify_empty(world, args.name)
    sidecar = build_twld(mod_root)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with outputs[0].open("xb") as f:
        f.write(world)
    with outputs[1].open("xb") as f:
        f.write(sidecar)
    reloaded = read_twld(outputs[1])
    assert reloaded["tiles"] == (10, {})
    assert reloaded["modData"] == mod_root["modData"]
    manifest = {"created_utc": dt.datetime.now(dt.timezone.utc).isoformat(),
                "template": str(args.template), "template_sha256": hashlib.sha256(vanilla).hexdigest(),
                "template_size": [original["width"], original["height"]], "world": header,
                "empty_tile_cells_verified": 8400 * 2400,
                "files": {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in outputs[:2]}}
    outputs[2].write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(manifest, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
