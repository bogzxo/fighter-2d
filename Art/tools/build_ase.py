# Builds the .ase files of the characters out of their sprite sheets, one frame of Aseprite per cell and one tag per animation.
# Aseprite itself writes the files (build_ase.lua run with its command line), this only works out which cell goes where.
#
#   python Art/tools/build_ase.py            every character
#   python Art/tools/build_ase.py the_male   only that one
#
# The mannequins (the_male, the_female) come as one huge sheet of 128 by 128 cells, ten to a row, every animation starting on a new row.
# The cut up sheets next to them are only good for the names. The ones of the male were cut a frame early and some are the same
# file under two names, so where every animation is was worked out from the big sheet and is written down below.
# The older characters come as a sheet with a definition.hor, a row per animation.

import os
import re
import subprocess
import sys

from PIL import Image

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
ASEPRITE = os.environ.get("ASEPRITE", r"C:\Program Files (x86)\Steam\steamapps\common\Aseprite\Aseprite.exe")
SCRIPT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "build_ase.lua")

# The animations that go round and round. Everything else plays once and stays on its last frame
LOOPS = {
    "idle", "run", "run_back", "run_loop", "crouch_idle", "crouch_walk", "crouch_walk_back", "crouch_loop", "block_loop", "fall",
    "falling_idle", "rope_hang", "edge_hang", "wall_hang", "climb_rope", "climb_rope_2", "climb_rope_3", "climb_down_rope", "push",
    "drill_spin",
}

# Where every animation of the male is on his sheet, first cell to last. death_1 has a hole in it, the sheet skips a row there
MALE = {
    "idle": [(0, 59)],
    "punch_straight": [(60, 104)],
    "crouch_walk": [(110, 150)],
    "punch_3": [(160, 185)],
    "punch_quad": [(190, 235)],
    "climb_rope_3": [(240, 359)],
    "climb_rope": [(360, 435)],
    "climb_down_rope": [(440, 517)],
    "climb_rope_2": [(520, 595)],
    "crouch_idle": [(600, 672)],
    "crouch_to_stand": [(680, 698)],
    "dodge_fwd": [(700, 730)],
    "death_2": [(740, 811)],
    "death_1": [(812, 841), (850, 890)],
    "roll": [(900, 970)],
    "falling_idle": [(980, 1000)],
    "falling_landing": [(1010, 1041)],
    "find_item": [(1050, 1462)],
    "rope_hang": [(1470, 1533)],
    "edge_hang": [(1540, 1679)],
    "wall_hang": [(1680, 1748)],
    "run_jump": [(1750, 1776)],
    "wall_jump": [(1780, 1817)],
    "kick_low": [(1820, 1864)],
    "kick_high": [(1870, 1909)],
    "kick_spin_high": [(1910, 1959)],
    "pull_heavy": [(1960, 1992)],
    "interact": [(2000, 2187)],
    "punch_1": [(2190, 2214)],
    "punch_2": [(2220, 2247)],
    "push": [(2250, 2489)],
    "run_back": [(2490, 2508)],
    "run": [(2510, 2526)],
    "kick_head": [(2530, 2581)],
}

FEMALE = {
    "idle": [(0, 249)],
    "run": [(250, 266)],
    "run_back": [(270, 288)],
    "rope_hang": [(290, 353)],
    "edge_hang": [(360, 499)],
    "wall_hang": [(500, 568)],
    "climb_rope": [(570, 629)],
    "climb_down_rope": [(630, 689)],
    "climb_rope_2": [(690, 723)],
    "crouch_walk": [(730, 770)],
    "crouch_idle": [(780, 852)],
    "crouch_to_stand": [(860, 878)],
    "punch_1": [(880, 904)],
    "punch_2": [(910, 937)],
    "punch_3": [(940, 965)],
    "punch_straight": [(970, 1014)],
    "punch_quad": [(1020, 1065)],
    "kick_low": [(1070, 1116)],
    "kick_high": [(1120, 1155)],
    "kick_spin_high": [(1160, 1211)],
    "kick_head": [(1220, 1271)],
    "find_item": [(1280, 1691)],
    "wall_jump": [(1700, 1737)],
    "dodge_fwd": [(1740, 1768)],
    "dodge_back": [(1770, 1818)],
    "roll": [(1820, 1891)],
    "push": [(1900, 2139)],
    "interact": [(2140, 2326)],
    "falling_idle": [(2330, 2351)],
    "falling_landing": [(2360, 2391)],
    "pull_heavy": [(2400, 2432)],
    "push_button": [(2440, 2536)],
    "run_jump": [(2540, 2565)],
    "death_1": [(2570, 2640)],
    "death_2": [(2650, 2721)],
}

# Tags that are made of the frames of another one. (name, the tag it shares its frames with, first and last frame of that tag
# or None for all of it, the way it plays). Walking backwards crouched is the crouch walk played the other way round, and so on
MANNEQUIN_EXTRAS = [
    ("crouch_walk_back", "crouch_walk", None, "reverse"),
    ("pull_lever", "interact", None, "forward"),

    # Nobody rendered them ducking or getting back up, so those are standing up and falling over played backwards
    ("crouch_down", "crouch_to_stand", None, "reverse"),
    ("get_up", "death_1", (34, 62), "reverse"),
]


def cells_of(ranges):
    cells = []
    for first, last in ranges:
        cells.extend(range(first, last + 1))
    return cells


def mannequin(name, sheet, table, seconds):
    """A character out of one big sheet of cells, by the table of where its animations are."""
    width = Image.open(sheet).size[0]
    frames, tags = [], []

    # In the order they are on the sheet
    for tag, ranges in sorted(table.items(), key=lambda item: item[1][0][0]):
        cells = cells_of(ranges)
        tags.append((tag, len(frames), len(frames) + len(cells) - 1, "forward"))
        frames.extend(cells)

    spans = {tag: (first, last) for tag, first, last, _ in tags}
    for tag, of, part, direction in MANNEQUIN_EXTRAS:
        if of not in spans:
            continue
        first, last = spans[of]
        if part is not None:
            first, last = first + part[0], first + part[1]
        tags.append((tag, first, last, direction))

    return {
        "name": name, "sheet": sheet, "cell": (128, 128), "columns": width // 128,
        "frames": frames, "tags": tags, "seconds": seconds,
    }


def legacy(name, directory, frame_rate):
    """A character out of a sheet with a definition.hor, where an animation is so many cells along a row."""
    text = open(os.path.join(directory, "definition.hor")).read()
    size = re.search(r"sprite_size:\s*\{\s*w:\s*(\d+),\s*h:\s*(\d+)", text)
    cell = (int(size.group(1)), int(size.group(2)))
    sheet = os.path.join(directory, "spritesheet.png")
    columns = Image.open(sheet).size[0] // cell[0]

    animations = []
    for found in re.finditer(r"(\w+):\s*\{\s*x:\s*(\d+),\s*y:\s*(\d+)(?:,\s*length:\s*(\d+))?", text):
        animations.append((found.group(1), int(found.group(2)), int(found.group(3)), int(found.group(4) or 1)))

    # Every cell any animation shows becomes a frame, row by row. Animations that share cells (block and block_start) share frames
    used = sorted({(y, x + i) for _, x, y, length in animations for i in range(length)})
    frame_of = {cell_at: index for index, cell_at in enumerate(used)}

    tags = [(tag, frame_of[(y, x)], frame_of[(y, x + length - 1)], "forward") for tag, x, y, length in animations]
    tags.sort(key=lambda tag: (tag[1], -tag[2]))

    return {
        "name": name, "sheet": sheet, "cell": cell, "columns": columns,
        "frames": [y * columns + x for y, x in used], "tags": tags, "seconds": 1.0 / frame_rate,
    }


def lua_string(text):
    return '"' + text.replace("\\", "/") + '"'


def build(character, output):
    os.makedirs(os.path.dirname(output), exist_ok=True)
    manifest = output + ".manifest.lua"

    with open(manifest, "w") as file:
        file.write("return {\n")
        file.write("  name = %s,\n  sheet = %s,\n  output = %s,\n" % (lua_string(character["name"]), lua_string(character["sheet"]), lua_string(output)))
        file.write("  cell_width = %d,\n  cell_height = %d,\n  columns = %d,\n  seconds = %.6f,\n" % (*character["cell"], character["columns"], character["seconds"]))
        file.write("  frames = {%s},\n" % ",".join(str(cell) for cell in character["frames"]))
        file.write("  tags = {\n")
        for tag, first, last, direction in character["tags"]:
            file.write("    { name = %s, from = %d, to = %d, direction = %s, loop = %s },\n" % (
                lua_string(tag), first + 1, last + 1, lua_string(direction), "true" if tag in LOOPS else "false"))
        file.write("  }\n}\n")

    try:
        subprocess.run([ASEPRITE, "-b", "--script-param", "manifest=" + manifest.replace("\\", "/"), "--script", SCRIPT], check=True)
    finally:
        os.remove(manifest)

    print("%s: %d frames, %d tags -> %s (%d KB)" % (
        character["name"], len(character["frames"]), len(character["tags"]), os.path.relpath(output, ROOT), os.path.getsize(output) // 1024))


def main():
    new = os.path.join(ROOT, "Fighters To Implement")
    characters = os.path.join(ROOT, "Assets", "sprites", "characters")
    art = os.path.join(ROOT, "Art", "characters")

    # The mannequins were rendered at 30 frames a second
    jobs = {
        "the_male": lambda: (mannequin("the_male", os.path.join(new, "The Male", "Male_spritesheet_all.png"), MALE, 1 / 30),
                             os.path.join(characters, "the_male", "the_male.ase")),
        "the_female": lambda: (mannequin("the_female", os.path.join(new, "The Female", "Female_spritesheet_all.png"), FEMALE, 1 / 30),
                               os.path.join(characters, "the_female", "the_female.ase")),
        # The sheets these two were made from are kept with the rest of the source art
        "cammy": lambda: (legacy("cammy", os.path.join(art, "cammy"), 12),
                          os.path.join(characters, "cammy", "cammy.ase")),
        "the_man": lambda: (legacy("the_man", os.path.join(art, "the_man"), 24),
                            os.path.join(characters, "the_man", "the_man.ase")),
    }

    wanted = sys.argv[1:] or list(jobs)
    for name in wanted:
        character, output = jobs[name]()
        build(character, output)


if __name__ == "__main__":
    main()
