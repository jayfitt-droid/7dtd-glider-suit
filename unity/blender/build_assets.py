# Builds the Glider Suit rig, textures, previews and the icon master in Blender.
#
#   blender -b --factory-startup -P unity/blender/build_assets.py -- <source dir> <preview dir> [<icon master png>]
#
# Outputs into <source dir> (the Unity project's Assets/Source):
#   glider_rig.json              the whole rig, already in Unity's coordinates:
#                                every bone and *_Folded pose (name, parent, local
#                                position, rotation, scale) and every mesh (parent,
#                                material kind, vertices, normals, UVs, triangles).
#                                The Unity build makes the prefab straight from it.
#                                There is no FBX: its importer turned the nested
#                                wing bones the wrong way (see plan.md, section 15).
#   glider_poles_albedo/normal/metallic.png   the frame atlas: steel, leather, rope, pack canvas
#   glider_tarp_albedo/normal/metallic.png    the wing canvas
# Preview renders go to <preview dir>; the 640x640 icon master goes to
# <icon master png> (default: tools/icon_master.png in the repo).
#
# The look (Jason's reference picture, 2026-10-12): a bat wing. One long leading
# spar per side, a row of ribs hanging back from it, tan canvas between the ribs
# with a scalloped trailing edge, and a strapped harness in the middle.
#
# How it folds, and why the pieces are rigid: the game moves the rig from code
# (GliderWings.cs). Every object named Spar_<side>_<n> is a bone; its sibling
# Spar_<side>_<n>_Folded holds the folded local rotation and scale, and the code
# blends between the two. Spar_<side>_0 is the leading spar and swings down the
# back; Spar_<side>_1.. are the ribs, children of the spar, which swing flat
# against it while their canvas panel is squeezed narrow. No skinning is needed.
#
# Blender space: +X = the player's left, -Y = forward, +Z = up, metres. Unity
# space: +X = right, +Y = up, +Z = forward. TO_UNITY below is that change of
# axes; it is a mirror (Blender is right-handed, Unity left-handed), so triangle
# winding is reversed on the way out. The rig origin is the attachment point on
# the upper back.
#
# The rig is authored on an upright body, and the wings are fixed to its back:
# open, they spread left and right and hang down behind like a cape, leading edge
# at the shoulders, tipped WING_TILT degrees back from the spine. In flight the
# game tips the body forward by about that much (body_pitch_base in the config),
# which is what lays them level. The grey-box rig had its wings in the upright
# body's horizontal plane instead, so they stood on end when the body lay down.

import json
import math
import os
import sys
import warnings

import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
SOURCE_DIR = os.path.abspath(argv[0] if len(argv) > 0 else "Assets/Source")
PREVIEW_DIR = os.path.abspath(argv[1] if len(argv) > 1 else SOURCE_DIR)
ICON_MASTER = os.path.abspath(argv[2] if len(argv) > 2 else os.path.join(REPO, "tools", "icon_master.png"))
os.makedirs(SOURCE_DIR, exist_ok=True)
os.makedirs(PREVIEW_DIR, exist_ok=True)

SEED = 20261012
ATLAS_TEX = 1024
TARP_TEX = 1024

# --- wing dimensions ----------------------------------------------------------
WING_TILT = 25.0                  # degrees the open wing leans back from the spine: 90 minus
                                  # body_pitch_base (65) in the mod's config, so the wing is
                                  # level when the body is tipped that far forward in flight
FLYING_PITCH = 90.0 - WING_TILT   # the body pitch at which the wing is level; previews use it
HUB = (0.10, 0.07, 0.06)          # wing root from the rig origin: left, back, up
SPAN = 2.50                       # one wing, metres
DIHEDRAL = 5.0                    # degrees the wing rises toward the tip
BOW = 0.42                        # how far the leading spar trails back at the tip
DROOP = 0.05                      # how far the tip hangs below the dihedral line
SPAR_RADIUS = 0.024
RIB_RADIUS = 0.014
RIB_T = (0.04, 0.20, 0.36, 0.52, 0.68, 0.82, 0.93)        # span stations
RIB_LENGTH = (1.10, 1.15, 1.10, 1.00, 0.86, 0.68, 0.48)   # chord at each rib
RIB_SPLAY = (2.0, 6.0, 10.0, 14.0, 19.0, 25.0, 32.0)      # degrees toward the tip
RIB_CAMBER = 0.045                # how far a rib arches upward
SCALLOP = 0.40                    # trailing-edge sag, as a share of the rib-tip gap
BILLOW = 0.035                    # how far the canvas bulges between ribs
PANEL_U, PANEL_V = 6, 6           # canvas grid per panel

# --- folded pose --------------------------------------------------------------
FOLD_SPAR_SCALE = 0.28            # the whole wing shrinks to hang as a bundle
FOLD_SPAR_DIR = (-0.04, 0.10, -1.0)  # down the back: inward, back, down (left wing; mirrored)
FOLD_RIB_SQUEEZE = 0.45           # canvas width left when a rib lies against the spar
FOLD_RIB_TWIST = 26.0             # degrees between neighbouring folded panels

# --- harness dimensions (an upright torso; tune here if it floats or sinks) ----
CHEST_Y = -0.245                  # front of the chest from the rig origin
STRAP_X = 0.10                    # shoulder straps, left and right
BELT_Z = -0.22
STRAP_WIDTH = 0.045

# --- the frame atlas: four horizontal bands, bottom to top ---------------------
BAND_STEEL, BAND_LEATHER, BAND_ROPE, BAND_PACK = 0, 1, 2, 3
BANDS = 4


def band_v(band, f):
    """A v coordinate inside an atlas band; f = 0..1 across the band, kept off its edges."""
    return (band + 0.08 + 0.84 * f) / BANDS


# ---------------------------------------------------------------- scene utils

def reset_scene():
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.images,
                  bpy.data.cameras, bpy.data.lights, bpy.data.armatures):
        for item in list(block):
            block.remove(item)
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 24
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    if scene.world is None:
        scene.world = bpy.data.worlds.new("World")
    return scene


def select_only(objs, active=None):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = active or objs[0]


def link(obj):
    bpy.context.scene.collection.objects.link(obj)
    return obj


def flat_material(name, colour, metallic, roughness):
    mat = bpy.data.materials.new(name)
    # use_nodes is deprecated in Blender 5 and its warning goes to stderr, which
    # build-assets.ps1 treats as a failure; it is still needed on older versions.
    with warnings.catch_warnings():
        warnings.simplefilter("ignore")
        mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*colour, 1.0)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    return mat


def textured_material(name, albedo_png, metallic, roughness):
    """Previews and the icon use the same albedo the game gets."""
    mat = flat_material(name, (0.6, 0.6, 0.6), metallic, roughness)
    tree = mat.node_tree
    tex = tree.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(os.path.join(SOURCE_DIR, albedo_png))
    tree.links.new(tex.outputs["Color"], tree.nodes.get("Principled BSDF").inputs["Base Color"])
    return mat


def mesh_obj(name, verts, faces, material, uvs, parent, parent_world, smooth=True):
    """A mesh whose vertices are given in world space and stored in its parent's space.
    Every vertex has exactly one UV and one normal, which is what the rig export
    writes: flat-shaded pieces are built with their own vertices per face."""
    inverse = parent_world.inverted()
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(inverse @ Vector(v)) for v in verts], [], faces)
    me.update()
    uv = me.uv_layers.new(name="UVMap")
    for poly in me.polygons:
        poly.use_smooth = smooth
        for li in poly.loop_indices:
            uv.data[li].uv = uvs[me.loops[li].vertex_index]
    obj = link(bpy.data.objects.new(name, me))
    obj.data.materials.append(material)
    obj.parent = parent
    obj["uvs"] = [c for uv_pair in uvs for c in uv_pair]
    return obj


def frame(origin, x_hint, y_axis):
    """A right-handed frame at origin: +Y along y_axis, +X as close to x_hint as it can be."""
    y = Vector(y_axis).normalized()
    x = Vector(x_hint)
    x = (x - y * x.dot(y)).normalized()
    z = x.cross(y)
    m = Matrix.Identity(4)
    for r in range(3):
        m[r][0], m[r][1], m[r][2], m[r][3] = x[r], y[r], z[r], origin[r]
    return m


def bone(name, world, parent, parent_world):
    """An empty placed at a world matrix. Returns the object."""
    obj = link(bpy.data.objects.new(name, None))
    obj.empty_display_size = 0.05
    obj.rotation_mode = "QUATERNION"
    obj.parent = parent
    obj.matrix_basis = parent_world.inverted() @ world
    return obj


def tube(name, points, radius, material, band, parent, parent_world, up=(0.0, 0.0, 1.0), segments=8, capped=True):
    """A round tube along a list of world points, mapped into one atlas band."""
    pts = [Vector(p) for p in points]
    verts, uvs, rings = [], [], []
    length = 0.0
    for i, p in enumerate(pts):
        if i > 0:
            length += (p - pts[i - 1]).length
        tangent = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        a = Vector(up)
        a = a - tangent * a.dot(tangent)
        if a.length < 1e-4:
            a = Vector((1.0, 0.0, 0.0)) - tangent * tangent.x
        a.normalize()
        b = tangent.cross(a)
        ring = []
        for j in range(segments + 1):
            ang = 2.0 * math.pi * j / segments
            ring.append(len(verts))
            verts.append(p + (a * math.cos(ang) + b * math.sin(ang)) * radius)
            uvs.append((length * 1.5, band_v(band, j / segments)))
        rings.append(ring)
    faces = []
    for i in range(len(rings) - 1):
        for j in range(segments):
            # Wound so the faces point outward; Blender's renderer does not care, the game's does.
            faces.append((rings[i][j], rings[i][j + 1], rings[i + 1][j + 1], rings[i + 1][j]))
    if capped:
        for ring, centre, flip in ((rings[0], pts[0], False), (rings[-1], pts[-1], True)):
            c = len(verts)
            verts.append(centre)
            uvs.append((0.0, band_v(band, 0.5)))
            for j in range(segments):
                faces.append((c, ring[j], ring[j + 1]) if flip else (c, ring[j + 1], ring[j]))
    return mesh_obj(name, verts, faces, material, uvs, parent, parent_world)


def ribbon(name, points, width_axis, width, material, band, parent, parent_world):
    """A flat strap along world points, as wide as given along width_axis, both faces."""
    pts = [Vector(p) for p in points]
    w = Vector(width_axis).normalized() * (width / 2.0)
    verts, uvs, faces = [], [], []
    length = 0.0
    for i, p in enumerate(pts):
        if i > 0:
            length += (p - pts[i - 1]).length
        verts += [p - w, p + w]
        uvs += [(length * 2.0, band_v(band, 0.0)), (length * 2.0, band_v(band, 1.0))]
    n = len(verts)
    verts += verts[:]
    uvs += uvs[:]
    for i in range(len(pts) - 1):
        a, b, c, d = 2 * i, 2 * i + 1, 2 * i + 3, 2 * i + 2
        faces.append((a, b, c, d))
        faces.append((n + d, n + c, n + b, n + a))
    return mesh_obj(name, verts, faces, material, uvs, parent, parent_world)


def box(name, size, centre, material, band, parent, parent_world):
    sx, sy, sz = (s / 2.0 for s in size)
    cx, cy, cz = centre
    corners = [(cx + x * sx, cy + y * sy, cz + z * sz)
               for x, y, z in ((-1, -1, -1), (1, -1, -1), (1, 1, -1), (-1, 1, -1),
                               (-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1))]
    quads = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    verts, uvs, faces = [], [], []
    for q, quad in enumerate(quads):
        base = len(verts)
        for k, index in enumerate(quad):
            verts.append(corners[index])
            uvs.append((0.15 + 0.2 * (k in (1, 2)) + 0.05 * q, band_v(band, 0.25 + 0.5 * (k in (2, 3)))))
        faces.append((base, base + 1, base + 2, base + 3))
    return mesh_obj(name, verts, faces, material, uvs, parent, parent_world, smooth=False)


# ------------------------------------------------------------------ textures

def blur(a, radius):
    """A cheap box blur via summed-area tables."""
    if radius < 1:
        return a
    pad = np.pad(a, radius, mode="edge")
    c = np.cumsum(np.cumsum(pad, axis=0), axis=1)
    c = np.pad(c, ((1, 0), (1, 0)))
    h, w = a.shape
    d = 2 * radius + 1
    return (c[d:d + h, d:d + w] - c[0:h, d:d + w] - c[d:d + h, 0:w] + c[0:h, 0:w]) / (d * d)


def noise(rng, size, radius):
    n = blur(rng.standard_normal((size, size)).astype(np.float32), radius)
    return n / (np.max(np.abs(n)) + 1e-6)


def write_png(name, size, rgba, data=False):
    img = bpy.data.images.new(name, size, size, alpha=True, float_buffer=False)
    img.colorspace_settings.name = "Non-Color" if data else "sRGB"
    flat = np.clip(rgba[::-1], 0.0, 1.0).astype(np.float32).ravel()   # Blender is bottom-up
    img.pixels.foreach_set(flat)
    path = os.path.join(SOURCE_DIR, name + ".png")
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)
    print("[assets] wrote", path)


def normal_from_height(height, strength):
    """A tangent-space normal map (OpenGL, +Y up) from a height field in image rows."""
    dx = np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)
    dy = np.roll(height, 1, axis=0) - np.roll(height, -1, axis=0)   # rows run downward
    nx, ny = -dx * strength, -dy * strength
    nz = np.ones_like(height)
    length = np.sqrt(nx * nx + ny * ny + nz * nz)
    out = np.zeros(height.shape + (4,), dtype=np.float32)
    out[..., 0] = nx / length * 0.5 + 0.5
    out[..., 1] = ny / length * 0.5 + 0.5
    out[..., 2] = nz / length * 0.5 + 0.5
    out[..., 3] = 1.0
    return out


def build_atlas_textures(rng):
    """Steel, leather, rope and pack canvas, one band each. u runs along a part."""
    size = ATLAS_TEX
    row, col = np.mgrid[0:size, 0:size].astype(np.float32)
    v = 1.0 - (row + 0.5) / size                 # image rows run downward, v upward
    band = np.clip((v * BANDS).astype(np.int32), 0, BANDS - 1)
    fine = noise(rng, size, 1)
    soft = noise(rng, size, 5)
    blotch = noise(rng, size, 14)
    two_pi = 2.0 * math.pi / size

    albedo = np.zeros((size, size, 4), dtype=np.float32)
    albedo[..., 3] = 1.0
    metal = np.zeros((size, size, 4), dtype=np.float32)
    height = np.zeros((size, size), dtype=np.float32)

    # Steel: dark, streaked along the tube, with rust where the blotch noise peaks.
    streak = 0.5 * np.sin(row * 0.9) + 0.3 * np.sin(row * 0.31 + 1.3) + 0.2 * fine
    rust = np.clip((blotch - 0.25) * 3.0, 0.0, 1.0)
    steel = np.stack([0.27 + 0.03 * streak, 0.26 + 0.03 * streak, 0.25 + 0.03 * streak], axis=-1)
    rusty = np.stack([0.40 + 0.06 * soft, 0.22 + 0.04 * soft, 0.11 + 0.02 * soft], axis=-1)
    steel = steel * (1.0 - rust[..., None]) + rusty * rust[..., None]

    # Leather: brown, pored, darker toward the strap edges.
    f = (v * BANDS) - band                       # 0..1 across a band
    edge = np.clip(1.0 - np.abs(f - 0.5) * 2.2, 0.0, 1.0) ** 0.35
    leather = np.stack([0.34 + 0.05 * soft, 0.21 + 0.035 * soft, 0.12 + 0.02 * soft], axis=-1)
    leather = leather * (0.62 + 0.38 * edge[..., None]) * (1.0 + 0.10 * fine[..., None])

    # Rope: pale hemp, strands running diagonally round the wrap.
    strand = np.sin((col * 18.0 + row * 46.0) * two_pi)
    rope = np.stack([0.62 + 0.10 * strand, 0.53 + 0.09 * strand, 0.36 + 0.07 * strand], axis=-1)
    rope = rope * (1.0 + 0.08 * fine[..., None])

    # Pack canvas: olive drab, a coarse weave, grubby.
    weave = np.sin(col * two_pi * 170.0) * np.sin(row * two_pi * 170.0)
    pack = np.stack([0.32 + 0.03 * weave, 0.33 + 0.03 * weave, 0.21 + 0.02 * weave], axis=-1)
    pack = pack * (1.0 + 0.16 * blotch[..., None])

    for index, colour in ((BAND_STEEL, steel), (BAND_LEATHER, leather), (BAND_ROPE, rope), (BAND_PACK, pack)):
        albedo[..., 0:3] = np.where((band == index)[..., None], colour, albedo[..., 0:3])

    metal[..., 0:3] = np.where((band == BAND_STEEL)[..., None], (0.78 * (1.0 - rust))[..., None], 0.0)
    metal[..., 3] = np.select(
        [band == BAND_STEEL, band == BAND_LEATHER, band == BAND_ROPE],
        [0.40 * (1.0 - 0.8 * rust), 0.30, 0.06], default=0.10)
    height = np.select(
        [band == BAND_STEEL, band == BAND_LEATHER, band == BAND_ROPE],
        [0.3 * rust + 0.05 * fine, 0.5 * fine, 0.8 * strand], default=0.5 * weave)

    write_png("glider_poles_albedo", size, albedo)
    write_png("glider_poles_normal", size, normal_from_height(height.astype(np.float32), 1.6), data=True)
    write_png("glider_poles_metallic", size, metal, data=True)


def build_tarp_textures(rng):
    """Tan wing canvas. u runs along the span (0 root, 1 tip), v from the leading
    edge (0) to the trailing edge (1), so the seams below land on the ribs."""
    size = TARP_TEX
    row, col = np.mgrid[0:size, 0:size].astype(np.float32)
    u = (col + 0.5) / size
    v = 1.0 - (row + 0.5) / size
    two_pi = 2.0 * math.pi / size
    weave = np.sin(col * two_pi * 210.0) * np.sin(row * two_pi * 210.0)
    fine = noise(rng, size, 1)
    stain = noise(rng, size, 22)
    blotch = noise(rng, size, 9)

    base = np.stack([np.full_like(u, 0.71), np.full_like(u, 0.63), np.full_like(u, 0.45)], axis=-1)
    # Sun-bleached toward the leading edge, grubbier toward the trailing edge.
    base = base * (1.06 - 0.22 * v[..., None] ** 1.5)
    # Large water stains and smaller dirt blotches.
    dark = np.clip(stain * 3.2 - 0.15, 0.0, 1.0)[..., None]
    base = base * (1.0 - 0.42 * dark) + np.array([0.33, 0.25, 0.15], dtype=np.float32) * (0.42 * dark)
    pale = np.clip(-stain * 3.0 - 0.2, 0.0, 1.0)[..., None]
    base = base * (1.0 + 0.10 * pale)
    base = base * (1.0 + 0.13 * blotch[..., None] + 0.045 * weave[..., None] + 0.04 * fine[..., None])

    height = 0.35 * weave + 0.25 * fine

    def darken(mask, amount):
        nonlocal base
        base = base * (1.0 - amount * mask[..., None])

    # Two sewn-on patches of other cloth: a faded olive one and a pale one.
    for (u0, u1, v0, v1, colour) in ((0.24, 0.33, 0.30, 0.52, (0.42, 0.43, 0.29)),
                                     (0.60, 0.67, 0.55, 0.78, (0.78, 0.74, 0.62))):
        inside = ((u > u0) & (u < u1) & (v > v0) & (v < v1)).astype(np.float32)
        tone = np.array(colour, dtype=np.float32) * (1.0 + 0.05 * weave[..., None] + 0.05 * blotch[..., None])
        base = base * (1.0 - inside[..., None]) + tone * inside[..., None]
        border = (((np.abs(u - u0) < 0.004) | (np.abs(u - u1) < 0.004)) & (v > v0) & (v < v1)) | \
                 (((np.abs(v - v0) < 0.004) | (np.abs(v - v1) < 0.004)) & (u > u0) & (u < u1))
        stitch = border & (((col + row) % 14) < 8)
        darken(stitch.astype(np.float32), 0.55)
        height += 0.6 * inside

    # A sleeve along every rib: a darker band with a row of stitches each side.
    for t in RIB_T:
        distance = np.abs(u - t)
        darken((distance < 0.011).astype(np.float32), 0.16)
        stitch = (np.abs(distance - 0.011) < 0.0016) & ((row % 16) < 9)
        darken(stitch.astype(np.float32), 0.50)
        height += 0.8 * (distance < 0.011)

    # Hems along the leading and trailing edges.
    for edge in (v < 0.035, v > 0.955):
        darken(edge.astype(np.float32), 0.14)
        height += 0.6 * edge
    darken(((np.abs(v - 0.035) < 0.002) | (np.abs(v - 0.955) < 0.002)) & ((col % 16) < 9), 0.50)

    albedo = np.zeros((size, size, 4), dtype=np.float32)
    albedo[..., 0:3] = np.clip(base, 0.0, 1.0)
    albedo[..., 3] = 1.0
    metallic = np.zeros((size, size, 4), dtype=np.float32)
    metallic[..., 3] = 0.12
    write_png("glider_tarp_albedo", size, albedo)
    write_png("glider_tarp_normal", size, normal_from_height(height.astype(np.float32), 1.2), data=True)
    write_png("glider_tarp_metallic", size, metallic, data=True)


# --------------------------------------------------------------------- model

class Wing:
    """The geometry of one wing in world space. side = +1 left, -1 right."""

    def __init__(self, side):
        self.side = side
        self.label = "L" if side > 0 else "R"
        dih = math.radians(DIHEDRAL)
        tilt = math.radians(WING_TILT)
        self.hub = Vector((side * HUB[0], HUB[1], HUB[2]))
        # Toward the trailing edge: down the back and a little away from it.
        self.chord = Vector((0.0, math.sin(tilt), -math.cos(tilt)))
        outward = Vector((float(side), 0.0, 0.0))
        # The wing's upper side in flight: the side facing away from the body.
        self.up = (outward.cross(self.chord) * side).normalized()
        self.span = (outward * math.cos(dih) + self.up * math.sin(dih)).normalized()   # toward the tip

    def spar_point(self, t):
        return (self.hub + self.span * (SPAN * t) + self.chord * (BOW * t ** 1.7)
                - self.up * (DROOP * t * t))

    def rib_axes(self, i):
        a = math.radians(RIB_SPLAY[i])
        along = (self.chord * math.cos(a) + self.span * math.sin(a)).normalized()
        tipward = (self.span * math.cos(a) - self.chord * math.sin(a)).normalized()
        return along, tipward

    def rib_point(self, i, v):
        """A point along rib i, v = 0 at the spar and 1 at the trailing tip."""
        if i >= len(RIB_T):                       # past the last rib: the spar tip itself
            return self.spar_point(1.0)
        along, _ = self.rib_axes(i)
        return (self.spar_point(RIB_T[i]) + along * (RIB_LENGTH[i] * v)
                + self.up * (RIB_CAMBER * math.sin(math.pi * v)))

    def station(self, i):
        return RIB_T[i] if i < len(RIB_T) else 1.0

    def panel_point(self, i, u, v):
        """The canvas between rib i and rib i + 1."""
        a, b = self.rib_point(i, v), self.rib_point(i + 1, v)
        p = a.lerp(b, u)
        # The leading edge follows the curved spar, not the chord between two ribs.
        t = self.station(i) + (self.station(i + 1) - self.station(i)) * u
        straight = self.spar_point(self.station(i)).lerp(self.spar_point(self.station(i + 1)), u)
        p += (self.spar_point(t) - straight) * (1.0 - v) ** 2
        # The trailing edge sags toward the spar between the rib tips: the bat-wing scallop.
        gap = (self.rib_point(i + 1, 1.0) - self.rib_point(i, 1.0)).length
        along, _ = self.rib_axes(min(i, len(RIB_T) - 1))
        p -= along * (SCALLOP * gap * 4.0 * u * (1.0 - u) * v ** 3)
        # Loaded canvas bulges upward between its ribs.
        p += self.up * (BILLOW * math.sin(math.pi * u) * math.sin(math.pi * (0.1 + 0.9 * v)))
        return p


def build_panel(name, wing, i, material, parent, parent_world):
    verts, uvs, faces = [], [], []
    for iv in range(PANEL_V + 1):
        for iu in range(PANEL_U + 1):
            u, v = iu / PANEL_U, iv / PANEL_V
            verts.append(wing.panel_point(i, u, v))
            t = wing.station(i) + (wing.station(i + 1) - wing.station(i)) * u
            uvs.append((t, v))                     # texture: u along the span, v leading to trailing
    upper = len(verts)
    # The underside is its own set of vertices so it lights correctly.
    verts += [p - wing.up * 0.004 for p in verts]
    uvs += uvs[:]
    row = PANEL_U + 1
    for iv in range(PANEL_V):
        for iu in range(PANEL_U):
            a, b = iv * row + iu, iv * row + iu + 1
            c, d = (iv + 1) * row + iu + 1, (iv + 1) * row + iu
            quad = (a, b, c, d) if wing.side > 0 else (d, c, b, a)
            faces.append(quad)
            faces.append(tuple(upper + k for k in reversed(quad)))
    return mesh_obj(name, verts, faces, material, uvs, parent, parent_world)


def build_wing(side, materials, root):
    """One wing: the leading spar bone, its rib bones, their meshes and the
    folded-pose empties the runtime reads."""
    wing = Wing(side)
    label = wing.label
    frame_mat, tarp_mat = materials["frame"], materials["tarp"]
    objects, fold = [], {}

    hub_world = Matrix.Translation(wing.hub)
    hub = bone("Hub_" + label, hub_world, root, Matrix.Identity(4))
    objects.append(hub)

    # --- the leading spar ---
    spar_world = frame(wing.hub, wing.chord, wing.span)
    spar = bone("Spar_%s_0" % label, spar_world, hub, hub_world)
    objects.append(spar)

    spar_points = [wing.spar_point(k / 14.0) for k in range(15)]
    objects.append(tube("Pole_%s_Spar" % label, spar_points, SPAR_RADIUS, frame_mat, BAND_STEEL,
                        spar, spar_world, up=wing.up))
    # A lighter second rod under the inner spar, tied to it: a homemade truss.
    def brace_point(t):
        fade = max(0.0, 1.0 - t / 0.72)
        return wing.spar_point(t) + wing.chord * (0.085 * fade) - wing.up * (0.060 * fade)
    brace_points = [brace_point(0.72 * k / 10.0) for k in range(11)]
    objects.append(tube("Pole_%s_Brace" % label, brace_points, 0.011, frame_mat, BAND_STEEL,
                        spar, spar_world, up=wing.up))
    for k, t in enumerate((0.10, 0.26, 0.42, 0.58)):
        objects.append(tube("Pole_%s_Tie%d" % (label, k), [wing.spar_point(t), brace_point(t)], 0.008,
                            frame_mat, BAND_STEEL, spar, spar_world, up=wing.span, segments=6))
    # Rope lashings where each rib meets the spar, and one binding the tip.
    for k, t in enumerate(list(RIB_T) + [0.995]):
        a, b = wing.spar_point(max(0.0, t - 0.014)), wing.spar_point(min(1.0, t + 0.014))
        objects.append(tube("Pole_%s_Lash%d" % (label, k), [a, b], SPAR_RADIUS + 0.011, frame_mat, BAND_ROPE,
                            spar, spar_world, up=wing.up, segments=10))

    folded_dir = Vector((-side * FOLD_SPAR_DIR[0], FOLD_SPAR_DIR[1], FOLD_SPAR_DIR[2]))
    spar_folded = frame(wing.hub, wing.up, folded_dir) @ Matrix.Diagonal((FOLD_SPAR_SCALE,) * 3 + (1.0,))
    pose = bone("Spar_%s_0_Folded" % label, spar_folded, hub, hub_world)
    objects.append(pose)
    fold["Spar_%s_0" % label] = {"direction": list(folded_dir.normalized()), "folded_scale": [FOLD_SPAR_SCALE] * 3}

    # --- the ribs, each carrying the canvas out to the next rib ---
    count = len(RIB_T)
    for i in range(count):
        along, tipward = wing.rib_axes(i)
        origin = wing.spar_point(RIB_T[i])
        rib_world = frame(origin, tipward, along)
        name = "Spar_%s_%d" % (label, i + 1)
        rib = bone(name, rib_world, spar, spar_world)
        objects.append(rib)

        rib_points = [wing.rib_point(i, k / 9.0) for k in range(10)]
        objects.append(tube("Pole_%s_Rib%d" % (label, i), rib_points, RIB_RADIUS, frame_mat, BAND_STEEL,
                            rib, rib_world, up=wing.up))
        tip_a, tip_b = wing.rib_point(i, 0.955), wing.rib_point(i, 1.0)
        objects.append(tube("Pole_%s_RibTip%d" % (label, i), [tip_a, tip_b], RIB_RADIUS + 0.007, frame_mat,
                            BAND_ROPE, rib, rib_world, up=wing.up, segments=8))
        objects.append(build_panel("Tarp_%s_%d" % (label, i), wing, i, tarp_mat, rib, rib_world))

        # Folded: the rib lies along the spar, pointing at the tip, its canvas
        # squeezed narrow and twisted round the bundle a little more than its neighbour's.
        twist = math.radians(FOLD_RIB_TWIST * (i - (count - 1) / 2.0))
        normal = rib_world.col[2].xyz
        across = -wing.chord * math.cos(twist) + normal * math.sin(twist)
        folded = frame(origin, across, wing.span) @ Matrix.Diagonal((FOLD_RIB_SQUEEZE, 1.0, 1.0, 1.0))
        pose = bone(name + "_Folded", folded, spar, spar_world)
        objects.append(pose)
        fold[name] = {"direction": list(wing.span), "folded_scale": [FOLD_RIB_SQUEEZE, 1.0, 1.0]}

    return objects, fold


def build_harness(materials, root):
    """Shoulder straps, a chest strap and buckle, a waist belt, and on the back a
    plate, a rolled canvas pack and the crossbar the wings hang from. Rigid. Every
    name starts with Harness: the first-person view hides those pieces."""
    frame_mat, metal = materials["frame"], materials["metal"]
    world = Matrix.Identity(4)
    parts = []

    # Shoulder straps: from the back plate over the shoulder and down the chest to the belt.
    profile = [(0.055, 0.14), (0.045, 0.21), (0.000, 0.262), (-0.080, 0.275), (-0.170, 0.235),
               (-0.225, 0.150), (CHEST_Y, 0.040), (-0.240, -0.090), (-0.228, BELT_Z)]
    for label, x in (("L", STRAP_X), ("R", -STRAP_X)):
        parts.append(ribbon("HarnessStrap" + label, [(x, y, z) for y, z in profile], (1, 0, 0), STRAP_WIDTH,
                            frame_mat, BAND_LEATHER, root, world))
        # Down the back from the plate to the belt.
        back = [(x, 0.055, 0.14), (x * 1.05, 0.060, -0.05), (x * 1.15, 0.045, BELT_Z)]
        parts.append(ribbon("HarnessBack" + label, back, (1, 0, 0), STRAP_WIDTH, frame_mat, BAND_LEATHER, root, world))

    chest = [(STRAP_X * (2.0 * k / 6.0 - 1.0), CHEST_Y - 0.004 - 0.006 * math.sin(math.pi * k / 6.0), 0.040)
             for k in range(7)]
    parts.append(ribbon("HarnessChest", chest, (0, 0, 1), STRAP_WIDTH * 0.85, frame_mat, BAND_LEATHER, root, world))
    parts.append(box("HarnessBuckle", (0.058, 0.016, 0.050), (0.0, CHEST_Y - 0.016, 0.040), metal, BAND_STEEL, root, world))

    belt = []
    for k in range(25):
        ang = 2.0 * math.pi * k / 24.0
        belt.append((0.178 * math.sin(ang), -0.095 - 0.142 * math.cos(ang), BELT_Z))
    parts.append(ribbon("HarnessBelt", belt, (0, 0, 1), STRAP_WIDTH * 1.15, frame_mat, BAND_LEATHER, root, world))
    parts.append(box("HarnessBeltBuckle", (0.066, 0.016, 0.060), (0.0, -0.250, BELT_Z), metal, BAND_STEEL, root, world))

    # The back: a stiff plate, the rolled pack lashed across it, the crossbar on top.
    parts.append(box("HarnessPlate", (0.27, 0.022, 0.30), (0.0, 0.060, 0.02), frame_mat, BAND_LEATHER, root, world))
    roll = [(-0.155 + 0.31 * k / 6.0, 0.118, -0.050) for k in range(7)]
    parts.append(tube("HarnessRoll", roll, 0.060, frame_mat, BAND_PACK, root, world, segments=12))
    for k, x in enumerate((-0.095, 0.095)):
        parts.append(tube("HarnessRollStrap%d" % k, [(x - 0.016, 0.118, -0.050), (x + 0.016, 0.118, -0.050)], 0.064,
                          frame_mat, BAND_LEATHER, root, world, segments=12, capped=False))
    bar = [(-0.150, HUB[1], HUB[2]), (0.150, HUB[1], HUB[2])]
    parts.append(tube("HarnessCrossbar", bar, 0.021, frame_mat, BAND_STEEL, root, world))
    for k, x in enumerate((-HUB[0], HUB[0])):
        parts.append(tube("HarnessClamp%d" % k, [(x - 0.022, HUB[1], HUB[2]), (x + 0.022, HUB[1], HUB[2])], 0.034,
                          frame_mat, BAND_ROPE, root, world, segments=10))
    return parts


def build():
    materials = {
        "tarp": textured_material("Tarp", "glider_tarp_albedo.png", 0.0, 0.85),
        "frame": textured_material("Frame", "glider_poles_albedo.png", 0.25, 0.60),
        "metal": flat_material("Metal", (0.50, 0.44, 0.30), 0.9, 0.38),     # dull brass
    }
    root = link(bpy.data.objects.new("GliderSuitRig", None))
    objects = build_harness(materials, root)
    fold_pose = {}
    for side in (1, -1):
        wing_objects, fold = build_wing(side, materials, root)
        objects += wing_objects
        fold_pose.update(fold)

    triangles = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objects if o.type == "MESH")
    print("[assets] model: %d objects, %d triangles, %d bones" % (len(objects), triangles, len(fold_pose)))
    return root, objects


# Blender (x left, y back, z up) to Unity (x right, y up, z forward).
TO_UNITY = Matrix(((-1.0, 0.0, 0.0, 0.0), (0.0, 0.0, 1.0, 0.0), (0.0, -1.0, 0.0, 0.0), (0.0, 0.0, 0.0, 1.0)))


def material_kind(obj):
    name = obj.data.materials[0].name
    return {"Tarp": "tarp", "Metal": "metal"}.get(name, "frame")


def export(root, objects):
    """Writes glider_rig.json. Bones keep their hierarchy; the meshes under one
    bone that share a material are merged, so the prefab has about thirty
    renderers instead of a hundred and twenty."""
    from_unity = TO_UNITY.inverted()
    bones, groups, order = [], {}, []
    for obj in objects:
        parent = obj.parent.name if obj.parent is not None and obj.parent is not root else ""
        if obj.type == "EMPTY":
            # A local matrix L in Blender axes is C L C^-1 in Unity axes. C is a signed
            # permutation, so the result still splits cleanly into move, turn and scale.
            loc, rot, scale = (TO_UNITY @ obj.matrix_basis @ from_unity).decompose()
            bones.append({
                "name": obj.name, "parent": parent,
                "position": [round(c, 6) for c in loc],
                "rotation": [round(c, 7) for c in (rot.x, rot.y, rot.z, rot.w)],
                "scale": [round(c, 6) for c in scale],
            })
            continue
        kind = material_kind(obj)
        key = (parent, kind)
        if key not in groups:
            groups[key] = {"vertices": [], "normals": [], "uvs": [], "triangles": [], "harness": True}
            order.append(key)
        group = groups[key]
        group["harness"] = group["harness"] and obj.name.startswith("Harness")
        base = len(group["vertices"]) // 3
        me = obj.data
        for vertex in me.vertices:
            p = TO_UNITY @ vertex.co
            n = (TO_UNITY.to_3x3() @ vertex.normal).normalized()
            group["vertices"] += [round(c, 5) for c in p]
            group["normals"] += [round(c, 4) for c in n]
        group["uvs"] += [round(c, 5) for c in obj["uvs"]]
        for poly in me.polygons:
            v = list(poly.vertices)
            for k in range(1, len(v) - 1):
                # Reversed: the change of axes is a mirror.
                group["triangles"] += [base + v[0], base + v[k + 1], base + v[k]]

    meshes = []
    for parent, kind in order:
        group = groups[(parent, kind)]
        label = {"tarp": "Tarp", "metal": "Buckles", "frame": "Frame"}[kind]
        # The first-person view hides everything whose name starts with Harness.
        name = ("Harness" + label) if (parent == "" and group["harness"]) else (label + "_" + (parent or "Root"))
        meshes.append({"name": name, "parent": parent, "kind": kind,
                       "vertices": group["vertices"], "normals": group["normals"],
                       "uvs": group["uvs"], "triangles": group["triangles"]})

    path = os.path.join(SOURCE_DIR, "glider_rig.json")
    with open(path, "w", encoding="utf-8") as handle:
        json.dump({"bones": bones, "meshes": meshes}, handle, separators=(",", ":"))
    print("[assets] wrote %s: %d bones, %d meshes" % (path, len(bones), len(meshes)))


# ------------------------------------------------------------------- preview

def setup_preview():
    scene = bpy.context.scene
    scene.render.film_transparent = True
    world = scene.world
    if getattr(world, "node_tree", None) is None:
        world.use_nodes = True
    world.node_tree.nodes.clear()
    out = world.node_tree.nodes.new("ShaderNodeOutputWorld")
    bg = world.node_tree.nodes.new("ShaderNodeBackground")
    bg.inputs["Color"].default_value = (0.62, 0.64, 0.68, 1.0)
    bg.inputs["Strength"].default_value = 1.0
    world.node_tree.links.new(bg.outputs["Background"], out.inputs["Surface"])
    sun = link(bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN")))
    sun.data.energy = 4.0
    sun.data.angle = math.radians(6.0)
    sun.rotation_euler = (math.radians(48), math.radians(10), math.radians(-150))
    rim = link(bpy.data.objects.new("Rim", bpy.data.lights.new("Rim", "SUN")))
    rim.data.energy = 1.6
    rim.rotation_euler = (math.radians(-55), math.radians(0), math.radians(40))
    cam = link(bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")))
    scene.camera = cam
    return cam


def shot(cam, path, location, target, lens=45, size=512, ortho=None, roll=0.0, samples=24):
    """Renders from location toward target. ortho = the orthographic width in metres."""
    scene = bpy.context.scene
    scene.cycles.samples = samples
    scene.render.resolution_x = scene.render.resolution_y = size
    direction = Vector(target) - Vector(location)
    cam.rotation_mode = "QUATERNION"
    cam.rotation_quaternion = direction.to_track_quat("-Z", "Y") @ Quaternion((0.0, 0.0, 1.0), math.radians(roll))
    cam.location = location
    if ortho is not None:
        cam.data.type = "ORTHO"
        cam.data.ortho_scale = ortho
    else:
        cam.data.type = "PERSP"
        cam.data.lens = lens
    cam.data.clip_start = 0.01
    cam.data.clip_end = 100.0
    scene.render.filepath = path if os.path.isabs(path) else os.path.join(PREVIEW_DIR, path)
    bpy.ops.render.render(write_still=True)
    print("[assets] preview", scene.render.filepath)


def bone_poses():
    """Every Spar_* bone with its extended (as authored) and folded local matrices."""
    poses = {}
    for obj in bpy.data.objects:
        if not obj.name.startswith("Spar_") or obj.name.endswith("_Folded"):
            continue
        folded = bpy.data.objects.get(obj.name + "_Folded")
        if folded is not None:
            poses[obj.name] = (obj.matrix_basis.copy(), folded.matrix_basis.copy())
    return poses


def apply_pose(poses, t):
    """t = 0 extended, 1 folded; rotation and scale blend, as GliderWings does in game."""
    for name, (extended, folded) in poses.items():
        l0, q0, s0 = extended.decompose()
        _, q1, s1 = folded.decompose()
        bpy.data.objects[name].matrix_basis = Matrix.LocRotScale(l0, q0.slerp(q1, t), s0.lerp(s1, t))


def preview(cam, root):
    poses = bone_poses()

    # Upright, as worn standing: the harness, and the open wings hanging like a cape.
    apply_pose(poses, 0.0)
    shot(cam, "harness_front.png", (0.45, -1.15, 0.30), (0.0, -0.10, 0.0), lens=55)
    shot(cam, "harness_back.png", (-0.50, 1.20, 0.35), (0.0, 0.0, 0.0), lens=55)
    shot(cam, "standing_open_back.png", (1.8, 5.2, 0.6), (0.0, 0.0, -0.45), lens=45, size=768)
    shot(cam, "standing_open_side.png", (5.0, 0.6, 0.2), (0.0, 0.3, -0.45), lens=45, size=768)

    # Tipped forward as the body is in flight, which lays the wings level.
    root.rotation_euler = (math.radians(FLYING_PITCH), 0.0, 0.0)
    shot(cam, "extended_top.png", (0.0, 0.5, 7.0), (0.0, 0.5, 0.0), ortho=5.6, size=768)
    shot(cam, "extended_front.png", (0.0, -6.0, 1.6), (0.0, 0.3, 0.1), lens=50, size=768)
    shot(cam, "extended_under.png", (2.4, -3.4, -2.2), (0.0, 0.5, 0.0), lens=45, size=768)
    shot(cam, "wing_close.png", (1.5, -1.2, 1.1), (1.1, 0.45, 0.05), lens=40, size=768)

    # The icon master: the open wing seen from above and one side, so the span is
    # foreshortened and the ribs stay readable at 160 pixels, laid on a diagonal
    # like Jason's reference picture. tools/make_icon.py trims and scales it.
    shot(cam, ICON_MASTER, (1.6, -3.0, 5.0), (0.0, 0.45, 0.0), ortho=5.0, size=640, roll=45.0, samples=96)
    root.rotation_euler = (0.0, 0.0, 0.0)

    apply_pose(poses, 1.0)
    shot(cam, "folded_back.png", (0.0, 1.9, 0.0), (0.0, 0.0, -0.35), lens=50)
    shot(cam, "folded_side.png", (1.6, 1.0, 0.0), (0.0, 0.05, -0.35), lens=50)

    # A five-frame fold sequence so the motion can be checked without the game.
    for index in range(5):
        apply_pose(poses, index / 4.0)
        shot(cam, "fold_%d.png" % index, (2.8, 3.4, 1.6), (0.0, 0.3, -0.2), lens=40, size=384)
    apply_pose(poses, 0.0)


def main():
    reset_scene()
    rng = np.random.default_rng(SEED)
    build_atlas_textures(rng)
    build_tarp_textures(rng)
    root, objects = build()
    export(root, objects)
    cam = setup_preview()
    preview(cam, root)
    print("[assets] done")


main()
