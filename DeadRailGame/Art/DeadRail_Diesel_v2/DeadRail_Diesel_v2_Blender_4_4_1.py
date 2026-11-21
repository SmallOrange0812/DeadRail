# -*- coding: utf-8 -*-
"""DeadRail v2 / Blender 4.4.1. P2 camera, P3 roof cage, three-way route and blackout tunnel.
Run in Blender Text Editor with Alt+P. Creates a new scene; preserves existing scenes.
14.4 m locomotive, orange/charcoal diesel styling, six axles, short nose and raised cab.
Walkable engine room: floor Z=1.38 m; 2.35 m clear height; right-hand service aisle.
V12 diesel and adjacent removable battery on left; P1/P2 cab, P4 forward engine room.
Two genuine 1.4 m wide side entry openings with parked sliding doors and steps.
Rear roof gun mount, access ladder and roof hatch. Ladder traversal requires Unity logic.
TrackSwitch_ROOT is a separate shootable prop with target pivot and LEFT/STRAIGHT/RIGHT indicators. Hollow tunnel with separate barricades and splinter meshes.
Original straight/curved/turnout rail modules are preserved.
Blender +Y forward / +Z up; FBX -Z forward / Y up; metres.
Static art only: shooting, colliders, player movement and networking need Unity integration.
Console (current script execution only):
 bpy.app.driver_namespace['deadrail_cutaway'](True)
 bpy.app.driver_namespace['deadrail_blackout'](True)
 bpy.app.driver_namespace['deadrail_shot_switch']('RIGHT')
 bpy.app.driver_namespace['deadrail_export']()
After reopening .blend, use collection/object visibility or rerun this generator for helpers.
SAVE_BLEND and EXPORT_FBX default False. Saving uses a new timestamp folder.
"""

import bpy
import math
from pathlib import Path
from datetime import datetime
from mathutils import Vector

# ---------------------- 사용자 설정 ----------------------
CUTAWAY = False
BLACKOUT = False
EXPORT_FBX = False
SAVE_BLEND = False
OUTPUT_DIR = ""  # 빈 값: 사용자 홈/DeadRail_Output. 예: r"D:\DeadRail_Assets"
TRACK_LENGTH = 8.0
TRACK_SEGMENTS = 7
CURVE_RADIUS = 24.0
CURVE_ANGLES = (30, 60)
TURNOUT_RADIUS = 48.0
TURNOUT_ANGLE = 20.0
TURNOUT_LEAD = 3.0
SLEEPER_SPACING = 2.0 / 3.0
START_IN_TRACK_VIEW = False
TRAIN_LENGTH = 14.4
RAIL_GAUGE = 1.435
CYLINDER_SIDES = 10

if TRACK_LENGTH <= 0 or not isinstance(TRACK_SEGMENTS, int) or TRACK_SEGMENTS < 1:
    raise ValueError('TRACK_LENGTH는 양수, TRACK_SEGMENTS는 1 이상의 정수여야 합니다.')
if RAIL_GAUGE <= 0 or SLEEPER_SPACING <= 0:
    raise ValueError('궤간과 침목 간격은 양수여야 합니다.')
if min(CURVE_RADIUS, TURNOUT_RADIUS) <= max(1.65, RAIL_GAUGE / 2 + 0.10):
    raise ValueError('반지름이 너무 작습니다. 도상 폭과 궤간보다 크게 설정하세요.')
if not CURVE_ANGLES or any(not 5 <= angle <= 90 for angle in CURVE_ANGLES):
    raise ValueError('CURVE_ANGLES에는 5~90도 회전각을 지정하세요.')
if len(set(CURVE_ANGLES)) != len(CURVE_ANGLES):
    raise ValueError('CURVE_ANGLES의 중복 회전각을 제거하세요.')
if not 10 <= TURNOUT_ANGLE <= 45 or TURNOUT_LEAD <= 0:
    raise ValueError('TURNOUT_ANGLE은 10~45도, TURNOUT_LEAD는 양수여야 합니다.')
if TURNOUT_RADIUS * (1 - math.cos(math.radians(TURNOUT_ANGLE))) < RAIL_GAUGE + 0.30:
    raise ValueError('분기 출구가 너무 가깝습니다. TURNOUT_ANGLE 또는 TURNOUT_RADIUS를 늘리세요.')

# ---------------------- 씬과 데이터 ----------------------
if bpy.app.version < (4, 4, 0):
    raise RuntimeError("이 스크립트는 Blender 4.4 이상을 대상으로 합니다.")
if bpy.context.object and bpy.context.object.mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')

SCENE = bpy.data.scenes.new("DeadRail_LowPoly")
SCENE.unit_settings.system = 'METRIC'
SCENE.unit_settings.scale_length = 1.0
SCENE.render.engine = 'BLENDER_EEVEE_NEXT'
SCENE.render.resolution_x = 1600
SCENE.render.resolution_y = 1000
SCENE.render.resolution_percentage = 100
if bpy.context.window:
    bpy.context.window.scene = SCENE

WORLD = bpy.data.worlds.new(SCENE.name + "_World")
WORLD.use_nodes = True
WORLD.node_tree.nodes['Background'].inputs['Color'].default_value = (0.075, 0.10, 0.15, 1)
WORLD.node_tree.nodes['Background'].inputs['Strength'].default_value = 0.45
SCENE.world = WORLD

def collection(name):
    c = bpy.data.collections.new(name)
    SCENE.collection.children.link(c)
    return c

TRAIN = collection("DR_01_Train")
TRACK = collection("DR_02_Track_Modules")
ENV = collection("DR_03_Preview_Environment")
RIG = collection("DR_04_Preview_Cameras_Lights")
JUNCTIONS = collection("DR_05_Curves_And_Turnouts")

def material(key, color, metallic=0.0, emission=0.0, alpha=1.0):
    m = bpy.data.materials.new(SCENE.name + "_" + key)
    m.diffuse_color = (*color, alpha)
    m.use_nodes = True
    p = m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value = (*color, alpha)
    p.inputs['Metallic'].default_value = metallic
    p.inputs['Roughness'].default_value = 0.78 if metallic < 0.5 else 0.48
    p.inputs['Emission Color'].default_value = (*color, 1)
    p.inputs['Emission Strength'].default_value = emission
    p.inputs['Alpha'].default_value = alpha
    if alpha < 1.0:
        m.surface_render_method = 'DITHERED'
    return m

M = {
    'body': material('Paint_Old_Ivory', (0.52, 0.235, 0.055)),
    'red': material('Paint_Faded_Red', (0.055, 0.085, 0.095)),
    'steel': material('Steel', (0.22, 0.27, 0.28), 0.65),
    'dark': material('Charcoal', (0.035, 0.052, 0.062)),
    'rust': material('Rust', (0.30, 0.12, 0.055)),
    'floor': material('Floor', (0.14, 0.18, 0.18)),
    'yellow': material('Hazard_Yellow', (0.72, 0.46, 0.075)),
    'glass': material('Window_Glass', (0.12, 0.23, 0.26), 0.12, alpha=0.22),
    'screen': material('CRT_Green', (0.05, 0.36, 0.18), emission=1.2),
    'screen_text': material('CRT_Text', (0.42, 0.94, 0.54), emission=1.5),
    'lamp': material('Warm_Lamp', (1.0, 0.73, 0.32), emission=2.5),
    'emergency': material('Emergency_Amber', (0.85, 0.17, 0.025), emission=0.6),
    'blue': material('Battery_Blue', (0.085, 0.20, 0.27)),
    'wood': material('Sleeper_Wood', (0.17, 0.11, 0.075)),
    'ballast': material('Ballast', (0.18, 0.20, 0.22)),
    'ground': material('Ground', (0.085, 0.105, 0.11)),
}

class MeshBuilder:
    """여러 박스/원통을 한 메시로 묶어 선로의 오브젝트 수를 줄입니다."""
    def __init__(self):
        self.vertices, self.faces, self.face_materials, self.materials = [], [], [], []

    def add(self, vertices, faces, mat):
        offset = len(self.vertices)
        if mat not in self.materials:
            self.materials.append(mat)
        index = self.materials.index(mat)
        self.vertices.extend(vertices)
        self.faces.extend(tuple(offset + i for i in face) for face in faces)
        self.face_materials.extend([index] * len(faces))

    def box(self, center, size, mat, yaw=0.0):
        x, y, z = center
        a, b, c = (s / 2 for s in size)
        v = [(x-a,y-b,z-c),(x+a,y-b,z-c),(x+a,y+b,z-c),(x-a,y+b,z-c),
             (x-a,y-b,z+c),(x+a,y-b,z+c),(x+a,y+b,z+c),(x-a,y+b,z+c)]
        if yaw:
            cs, sn = math.cos(yaw), math.sin(yaw)
            v = [(x+(vx-x)*cs-(vy-y)*sn,
                  y+(vx-x)*sn+(vy-y)*cs, vz) for vx,vy,vz in v]
        f = [(3,2,1,0),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)]
        self.add(v, f, mat)

    def cylinder(self, radius, depth, mat, sides=CYLINDER_SIDES, top_radius=None):
        top = radius if top_radius is None else top_radius
        v = [(r*math.cos(i*math.tau/sides), r*math.sin(i*math.tau/sides), z)
             for r, z in [(radius,-depth/2),(top,depth/2)] for i in range(sides)]
        f = [tuple(reversed(range(sides))), tuple(range(sides,2*sides))]
        f += [(i,(i+1)%sides,(i+1)%sides+sides,i+sides) for i in range(sides)]
        self.add(v, f, mat)

    def build(self, name, target, parent=None, location=(0,0,0)):
        mesh = bpy.data.meshes.new(name + "_Mesh")
        mesh.from_pydata(self.vertices, [], self.faces)
        mesh.update()
        for mat in self.materials:
            mesh.materials.append(mat)
        for p, index in zip(mesh.polygons, self.face_materials):
            p.material_index = index
            p.use_smooth = False
        obj = bpy.data.objects.new(name, mesh)
        target.objects.link(obj)
        obj.parent = parent
        obj.location = location
        return obj

def empty(name, loc=(0,0,0), parent=None, target=TRAIN):
    o = bpy.data.objects.new(name, None)
    target.objects.link(o)
    o.parent, o.location = parent, loc
    o.empty_display_type = 'PLAIN_AXES'
    o.empty_display_size = 0.25
    return o

def box(name, loc, size, mat, parent=None, target=TRAIN, bevel=0.0, cut=None):
    b = MeshBuilder()
    b.box((0,0,0), size, M[mat])
    o = b.build(name, target, parent, loc)
    if bevel > 0:
        mod = o.modifiers.new('LowPoly_Edge', 'BEVEL')
        mod.width, mod.segments = bevel, 1
    if cut:
        o['dr_cutaway'] = cut
    return o

def cylinder(name, loc, radius, depth, mat, parent=None, rotation=(0,0,0), sides=CYLINDER_SIDES, top_radius=None, target=TRAIN):
    b = MeshBuilder()
    b.cylinder(radius, depth, M[mat], sides, top_radius)
    o = b.build(name, target, parent, loc)
    o.rotation_euler = rotation
    return o

def beam(name, a, b, width, mat, parent=None, target=TRAIN):
    a, b = Vector(a), Vector(b)
    delta = b-a
    o = box(name, (a+b)/2, (width,width,delta.length), mat, parent, target)
    o.rotation_euler = delta.to_track_quat('Z','Y').to_euler()
    return o

def label(name, text, loc, size, parent, mat='screen_text', rotation=(math.pi/2,0,0), target=TRAIN):
    font = bpy.data.curves.new(name + '_Font', 'FONT')
    font.body, font.size, font.extrude = text, size, 0
    font.materials.append(M[mat])
    o = bpy.data.objects.new(name, font)
    target.objects.link(o)
    o.parent, o.location, o.rotation_euler = parent, loc, rotation
    return o

def light(name, loc, energy, color, parent=None, kind='POINT', target=RIG):
    data = bpy.data.lights.new(name, kind)
    data.energy, data.color = energy, color
    o = bpy.data.objects.new(name, data)
    target.objects.link(o)
    o.parent, o.location = parent, loc
    return o

ROOT = empty('TrainRoot')
ROOT['description'] = 'Fixed origin treadmill train; Blender forward +Y'
ROOT['length_m'], ROOT['width_m'] = TRAIN_LENGTH, 3.9

# ---------------------- Walkable diesel locomotive ----------------------
# Floor top 1.38 m. Interior x=-1.74..1.74; service aisle x=-0.20..1.74.
ROOT['interior_clear_height_m'] = 2.35
ROOT['service_aisle_min_m'] = 1.55
ROOT['design'] = 'Orange / charcoal diesel, short nose, raised cab, long engine hood'
box('Underframe', (0,0,1.01), (3.3,14.4,0.26), 'dark', ROOT, bevel=0.05)
box('Interior_Floor', (0,0,1.28), (3.8,14.4,0.20), 'floor', ROOT)
for x in (-1.85,1.85):
    box('Yellow_Sill', (x,0,1.24), (0.12,14.4,0.24), 'yellow', ROOT)
for y in (-4.65,4.65):
    bogie = empty('Bogie_ROOT', (0,y,0), ROOT)
    box('Bogie_Frame', (0,0,0.81), (2.72,2.75,0.25), 'dark', bogie, bevel=0.07)
    for ay in (-0.92,0,0.92):
        cylinder('Axle', (0,ay,0.83), 0.10, 2.7, 'steel', bogie, (0,math.pi/2,0))
        for x in (-RAIL_GAUGE/2, RAIL_GAUGE/2):
            pivot = empty('Wheel_RotationPivot', (x,ay,0.83), bogie)
            pivot['rotation_axis'] = 'local X'
            cylinder('Wheel', (0,0,0), 0.45, 0.18, 'dark', pivot, (0,math.pi/2,0), sides=16)
        for x in (-1.38,1.38):
            cylinder('Axle_Box', (x,ay,0.83), 0.15, 0.14, 'steel', bogie, (0,math.pi/2,0))
            for z in (0.93,0.99,1.05):
                cylinder('Spring_Ring', (x,ay,z), 0.13, 0.035, 'dark', bogie)
    for x in (-1.4,1.4):
        box('Bogie_Side_Frame', (x,0,0.83), (0.13,2.68,0.15), 'steel', bogie)
box('Underbody_Fuel_Reservoir', (0,0,0.68), (2.4,4.4,0.66), 'dark', ROOT, bevel=0.18)

# Actual door apertures: no wall panels cross y=-5.6..-4.2.
for side in (-1,1):
    x = side*1.80
    cut = 'right_wall' if side == 1 else None
    for lo,hi in ((-6.25,-5.6),(-4.2,2.8)):
        box('Engine_Hood_Side', (x,(lo+hi)/2,2.55), (0.12,hi-lo,2.34), 'body', ROOT, cut=cut)
        box('Diesel_Black_Band', (side*1.869,(lo+hi)/2,2.1), (0.015,hi-lo,0.42), 'dark', ROOT, cut=cut)
    for y in (-3.6,-2.4,-1.2,0,1.2,2.3):
        box('Hood_Service_Panel', (side*1.874,y,2.77), (0.02,0.98,1.36), 'body', ROOT, cut=cut)
        for z in (2.52,2.64,2.76,2.88,3.0,3.12):
            box('Radiator_Louver', (side*1.894,y,z), (0.035,0.76,0.045), 'dark', ROOT, cut=cut)
        box('Panel_Latch', (side*1.915,y+0.39,2.38), (0.04,0.055,0.18), 'steel', ROOT, cut=cut)
    box('Door_Header', (x,-4.9,3.64), (0.14,1.4,0.16), 'yellow', ROOT, cut=cut)
    for y in (-5.65,-4.15):
        box('Door_Jamb', (x,y,2.47), (0.18,0.1,2.18), 'yellow', ROOT, cut=cut)
    # Sliding door leaf parked beside the opening, outside the hull.
    door = empty('Entry_SlidingDoor_'+str(side), (side*1.98,-3.43,1.4), ROOT)
    door['closed_local_y'] = -4.9
    door['open_local_y'] = -3.43
    door['clear_width_m'], door['clear_height_m'] = 1.4, 2.18
    box('Sliding_Door_Leaf', (0,0,1.02), (0.07,1.38,2.04), 'dark', door, cut=cut)
    box('Door_Inset', (side*0.045,0,1.02), (0.02,1.16,1.78), 'body', door, cut=cut)
    box('Door_Handle', (side*0.09,-0.48,0.96), (0.06,0.055,0.3), 'yellow', door, cut=cut)
    for i in range(5):
        box('Boarding_Stair', (side*(2.0+0.28*(4-i)),-4.9,0.28+i*0.22), (0.38,1.35,0.12), 'steel', ROOT)
    for y in (-5.57,-4.23):
        beam('Entry_Handrail', (side*3.18,y,1.15), (side*1.94,y,2.12), 0.055, 'yellow', ROOT)
    # Cab glazing only: solid long hood distinguishes this from a passenger car.
    box('Cab_Side_Lower', (x,4.19,2.01), (0.12,2.78,1.26), 'body', ROOT, cut=cut)
    box('Cab_Side_Stripe', (side*1.871,4.19,2.1), (0.025,2.78,0.42), 'dark', ROOT, cut=cut)
    for y in (2.87,4.2,5.51):
        box('Cab_Window_Post', (x,y,3.23), (0.14,0.1,1.20), 'dark', ROOT, cut=cut)
    for y in (3.53,4.86):
        box('Cab_Side_Glass', (x,y,3.22), (0.025,1.2,1.12), 'glass', ROOT, cut=cut)
    box('Cab_Top_Rail', (x,4.19,3.92), (0.14,2.78,0.22), 'body', ROOT, cut=cut)

# Front windscreen and short rounded-edge hood.
box('Cab_Front_Lower', (0,5.58,2.02), (3.6,0.14,1.28), 'body', ROOT)
for x in (-1.74,0,1.74):
    box('Windscreen_Pillar', (x,5.58,3.23), (0.12,0.14,1.18), 'dark', ROOT)
for x in (-0.87,0.87):
    box('Cab_Windscreen', (x,5.58,3.23), (1.60,0.03,1.08), 'glass', ROOT)
    beam('Windscreen_Wiper', (x-0.4,5.62,2.76), (x+0.25,5.62,3.14), 0.025, 'dark', ROOT)
box('Cab_Brow', (0,5.64,3.96), (3.82,0.36,0.28), 'body', ROOT, bevel=0.09, cut='roof')
box('Short_Nose', (0,6.22,2.0), (2.75,1.22,1.24), 'body', ROOT, bevel=0.19)
box('Nose_Black_Band', (0,6.84,1.91), (2.58,0.025,0.33), 'dark', ROOT)
box('Rear_Bulkhead', (0,-6.25,2.55), (3.6,0.12,2.34), 'body', ROOT, cut='rear_wall')
for y in (-7.1,7.1):
    box('Pilot_Buffer', (0,y,1.01), (3.65,0.24,0.42), 'dark', ROOT, bevel=0.06)
    for x in (-1.5,-1,-0.5,0,0.5,1,1.5):
        o=box('Pilot_Hazard_Stripe', (x,y+(0.13 if y>0 else -0.13),1.01), (0.22,0.02,0.37), 'yellow', ROOT)
        o.rotation_euler.y=math.radians(-25)
    box('Coupler_Shank', (0,y+(0.32 if y>0 else -0.32),0.92), (0.28,0.65,0.23), 'steel', ROOT)
    box('Coupler_Knuckle', (0.08,y+(0.61 if y>0 else -0.61),0.92), (0.40,0.28,0.31), 'dark', ROOT, bevel=0.05)
    for x in (-1.65,1.65):
        beam('End_Handrail_Post',(x,y,1.38),(x,y,2.38),0.055,'yellow',ROOT)
        beam('End_Handrail',(x,y,2.38),(x*0.36,y,2.38),0.055,'yellow',ROOT)
for x in (-1.05,1.05):
    cylinder('Headlight_Housing', (x,6.86,2.26), 0.19, 0.10, 'dark', ROOT, (-math.pi/2,0,0))
    cylinder('Headlight_Lens', (x,6.92,2.26), 0.135, 0.025, 'lamp', ROOT, (-math.pi/2,0,0))
    box('Number_Board',(x,5.84,3.96),(0.74,0.03,0.22),'dark',ROOT,cut='roof')
    label('Road_Number','041', (x+0.25,5.864,3.89),0.18,ROOT,'yellow',(math.pi/2,0,math.pi))['dr_cutaway']='roof'
roof = box('Engine_Roof_Removable', (0,-1.72,3.84), (3.85,9.15,0.24), 'dark', ROOT, bevel=0.09, cut='roof')
box('Cab_Roof_Removable', (0,4.22,4.15), (3.98,3.12,0.23), 'body', ROOT, bevel=0.09, cut='roof')
for y in (-2.8,-0.7,1.4):
    cylinder('Roof_Fan_Housing',(0,y,4.02),0.64,0.16,'steel',ROOT,sides=20)['dr_cutaway']='roof'
    cylinder('Roof_Fan_Dark',(0,y,4.11),0.55,0.025,'dark',ROOT,sides=20)['dr_cutaway']='roof'
    for angle in range(0,180,30):
        t=math.radians(angle)
        beam('Fan_Grille',(-0.55*math.cos(t),y-0.55*math.sin(t),4.14),(0.55*math.cos(t),y+0.55*math.sin(t),4.14),0.035,'steel',ROOT)['dr_cutaway']='roof'
cylinder('Diesel_Exhaust',(-1.1,-1.1,4.17),0.13,0.54,'dark',ROOT)['dr_cutaway']='roof'

# ---------------------- 역할별 실내 장비 ----------------------
def console_station(name, loc, title, second_line):
    r = empty(name, loc, ROOT)
    r['interaction'] = title
    box('Console_Base', (0,0,0.35), (0.77,0.64,0.70), 'steel', r, bevel=0.025)
    box('Console_Desk', (0,-0.04,0.74), (0.91,0.81,0.14), 'dark', r, bevel=0.025)
    box('CRT_Housing', (0,0.18,1.11), (0.76,0.30,0.58), 'dark', r, bevel=0.04)
    box('CRT_Screen', (0,0.018,1.13), (0.61,0.014,0.43), 'screen', r)
    label('CRT_Title', title, (-0.27,0.006,1.20), 0.075, r)
    label('CRT_Status', second_line, (-0.27,0.005,1.07), 0.055, r)
    label('CRT_Power', 'SYS ONLINE', (-0.27,0.005,0.98), 0.05, r)
    for x in (-0.25,-0.08,0.09):
        cylinder('Console_Button', (x,-0.24,0.83), 0.043, 0.028, 'yellow', r, sides=8)
    pivot = empty('Lever_Pivot', (0.29,-0.23,0.83), r)
    beam('Lever', (0,0,0), (0,-0.08,0.17), 0.035, 'steel', pivot)
    box('Lever_Grip', (0,-0.08,0.19), (0.09,0.06,0.06), 'red', pivot)
    box('Operator_Seat', (0,-0.89,0.48), (0.57,0.52,0.15), 'red', r, bevel=0.03)
    box('Seat_Back', (0,-1.1,0.77), (0.57,0.10,0.55), 'red', r, bevel=0.03)
    box('Seat_Pedestal', (0,-0.89,0.24), (0.21,0.25,0.47), 'steel', r)
    return r

DRIVE = console_station('P1_Drive_Console_ROOT', (-1.15,4.85,1.39), 'P1 DRIVE', 'FUEL 68  /  BAT 42')
RADAR = console_station('P4_Radar_Console_ROOT', (-1.15,2.15,1.39), 'P4 RADAR', 'NODE 03 / SIGNAL OK')
# 네모와 선으로 된 레이더 화면 그래픽: 실제 게임 UI 교체 자리입니다.
for x in (-0.15,0,0.15):
    box('Radar_Grid_Line', (x,0.003,1.115), (0.005,0.005,0.12), 'screen_text', RADAR)

SEARCH_CONTROL = console_station('P2_Searchlight_Control_ROOT', (1.15,4.85,1.39), 'P2 SEARCH', 'MANUAL AIM')
SEARCH = empty('P2_Searchlight_YawPivot', (1.15,4.9,4.34), ROOT)
SEARCH['interaction'] = 'Rotate yaw around Z; pitch around local X'
cylinder('Searchlight_Base', (0,0,0), 0.22, 0.17, 'steel', SEARCH)
PITCH = empty('Searchlight_PitchPivot', (0,0,0.38), SEARCH)
for x in (-0.23,0.23):
    box('Searchlight_Fork', (x,0,0.21), (0.075,0.14,0.58), 'yellow', SEARCH)
cylinder('Searchlight_Body', (0,0,0), 0.30, 0.45, 'dark', PITCH, (-math.pi/2,0,0))
cylinder('Searchlight_Lens', (0,0.24,0), 0.25, 0.02, 'lamp', PITCH, (-math.pi/2,0,0))
spot = light('Searchlight_Preview_Light', (0,0.27,0), 550, (1,0.81,0.51), PITCH, 'SPOT', TRAIN)
spot.rotation_euler = (math.pi/2,0,0)
spot.data.spot_size, spot.data.spot_blend = math.radians(38), 0.35
spot['dr_powered'] = True
spot['dr_energy'] = 550.0

WEAPON = empty('P3_Weapon_Mount_ROOT', (0,-5.38,3.97), ROOT)
WEAPON['interaction'] = 'Weapon pivot for shooting / harpoon placeholder'
cylinder('Weapon_Pedestal', (0,0,0.43), 0.14, 0.85, 'steel', WEAPON)
GUN = empty('P3_AimPivot', (0,0,1.0), WEAPON)
GUN.rotation_euler.z = math.pi/2
box('Weapon_Receiver', (0.19,0,0), (0.56,0.22,0.26), 'dark', GUN, bevel=0.025)
cylinder('Weapon_Barrel', (0.69,0,0.04), 0.065, 0.68, 'steel', GUN, (0,math.pi/2,0))
box('Weapon_Stock', (-0.22,0,-0.06), (0.35,0.20,0.20), 'rust', GUN)
box('Weapon_Grip', (0.08,0,-0.20), (0.12,0.10,0.25), 'dark', GUN)
cylinder('Harpoon_Spool', (0.21,-0.19,-0.06), 0.17, 0.12, 'yellow', GUN, (math.pi/2,0,0))
cylinder('Harpoon_Bolt', (0.55,0,0.19), 0.019, 0.72, 'steel', GUN, (0,math.pi/2,0))
cylinder('Harpoon_Tip', (0.97,0,0.19), 0.055, 0.15, 'steel', GUN,
         (0,math.pi/2,0), sides=6, top_radius=0.001)

GENERATOR = empty('Manual_Generator_ROOT', (0.85,-5.92,1.39), ROOT)
GENERATOR.scale = (0.5,0.5,0.5)
GENERATOR['interaction'] = 'Manual crank; pivot rotates around local X'
box('Generator_Housing', (0,0,0.47), (0.74,1.05,0.94), 'steel', GENERATOR, bevel=0.05)
box('Generator_Control', (0.04,-0.535,0.63), (0.49,0.035,0.33), 'dark', GENERATOR)
label('Generator_Label', 'MANUAL POWER', (-0.20,-0.56,0.66), 0.05, GENERATOR, 'yellow')
for gy in (-0.33,-0.11,0.11,0.33):
    box('Generator_Vent', (-0.382,gy,0.54), (0.022,0.055,0.47), 'dark', GENERATOR)
CRANK = empty('Generator_CrankPivot', (0.43,0,0.56), GENERATOR)
cylinder('Crank_Axle', (0,0,0), 0.055, 0.24, 'dark', CRANK, (0,math.pi/2,0))
box('Crank_Arm', (0.12,0,0.15), (0.06,0.07,0.30), 'yellow', CRANK)
cylinder('Crank_Handle', (0.22,0,0.29), 0.06, 0.20, 'dark', CRANK, (0,math.pi/2,0))

BATTERY_BAY = empty('Battery_Bay_ROOT', (-1.10,-3.62,1.40), ROOT)
box('Battery_Tray', (0,0,0.055), (0.82,1.12,0.11), 'steel', BATTERY_BAY)
for x in (-0.40,0.40):
    box('Battery_Tray_Rail', (x,0,0.16), (0.06,1.12,0.23), 'yellow', BATTERY_BAY)
box('Battery_Connector', (0,0.51,0.41), (0.51,0.12,0.51), 'dark', BATTERY_BAY)

BATTERY = empty('BatteryPack_30kg_ROOT', (-1.10,-3.64,1.53), ROOT)
BATTERY['mass_kg'] = 30.0
BATTERY['interaction'] = 'Pickup / Drop; use Heavy Walk in Unity'
box('Battery_Case', (0,0,0.34), (0.65,0.88,0.68), 'blue', BATTERY, bevel=0.04)
for y in (-0.30,0.30):
    box('Battery_Retaining_Band', (0,y,0.35), (0.68,0.07,0.71), 'dark', BATTERY)
for x in (-0.26,0.26):
    beam('Battery_Handle_Post', (x,0,0.70), (x,0,0.84), 0.05, 'steel', BATTERY)
beam('Battery_Handle', (-0.26,0,0.84), (0.26,0,0.84), 0.06, 'steel', BATTERY)
box('Battery_Status', (0,-0.449,0.45), (0.28,0.014,0.065), 'screen', BATTERY)
label('Battery_Weight', '30 KG', (-0.20,-0.46,0.24), 0.08, BATTERY, 'yellow')
for x in (-0.20,0.20):
    cylinder('Battery_Terminal', (x,0.26,0.73), 0.047, 0.09, 'steel', BATTERY)

FUEL = empty('Fuel_Tank_ROOT', (0,-5.2,0.87), ROOT)
FUEL['resource'] = 'Fuel; independent of main battery'
box('Fuel_Tank', (0,0,0), (1.05,1.70,0.42), 'rust', FUEL, bevel=0.08)
for y in (-0.6,0.6):
    box('Fuel_Strap', (0,y,0), (1.08,0.06,0.44), 'steel', FUEL)
box('Fuel_Fill_Point', (-1.40,-4.75,1.70), (0.09,0.30,0.27), 'yellow', ROOT)

# ---------------------- Diesel engine and clear service aisle ----------------------
ENGINE=empty('Diesel_Engine_ROOT',(-1.05,-1.10,1.4),ROOT)
ENGINE['interaction']='Repair / refuel diesel engine'
ENGINE['battery_neighbor']='Battery_Bay_ROOT; 0.28 m service gap along Y'
box('Engine_Skid',(0,0,0.10),(1.22,3.1,0.2),'dark',ENGINE,bevel=0.04)
box('Diesel_Crankcase',(0,0,0.48),(0.90,2.6,0.66),'steel',ENGINE,bevel=0.08)
for x in (-0.28,0.28):
    bank=box('Diesel_Cylinder_Bank',(x,0,0.96),(0.43,2.35,0.51),'dark',ENGINE,bevel=0.04)
    bank.rotation_euler.y=math.radians(-20 if x<0 else 20)
    for y in (-0.9,-0.54,-0.18,0.18,0.54,0.9):
        box('Cylinder_Head',(x,y,1.25),(0.40,0.29,0.17),'steel',ENGINE,bevel=0.025)
        beam('Injector_Line',(x,y,1.34),(x*0.3,y,1.46),0.024,'yellow',ENGINE)
    beam('Exhaust_Manifold',(x*1.65,-1.15,1.02),(x*1.65,1.15,1.02),0.12,'rust',ENGINE)
cylinder('Alternator',(0,1.18,0.67),0.40,0.58,'blue',ENGINE,(math.pi/2,0,0),sides=16)
cylinder('Turbocharger',(0,-0.87,1.47),0.22,0.42,'steel',ENGINE,(0,math.pi/2,0),sides=12)
for y in (-0.72,0.72):
    cylinder('Engine_Service_Filter',(0.55,y,0.65),0.11,0.33,'yellow',ENGINE)
box('Engine_Service_Plate',(0,-1.318,0.58),(0.68,0.025,0.26),'dark',ENGINE)
label('Diesel_Label','V12 DIESEL',(-0.29,-1.335,0.59),0.085,ENGINE,'yellow')
beam('Battery_Cable',(-0.7,-3.16,1.67),(-0.7,-2.72,1.67),0.045,'red',ROOT)
beam('Battery_Return',(-0.85,-3.16,1.61),(-0.85,-2.72,1.61),0.04,'dark',ROOT)
for y in (-5.7,-3.5,-1.3,0.9,3.1):
    for x in (-0.25,1.65):
        box('Aisle_Edge_Mark',(x,y,1.386),(0.045,1.7,0.009),'yellow',ROOT)
for y in (-4,0,3.4):
    box('Interior_Lamp',(0,y,3.67),(0.70,0.17,0.035),'lamp',ROOT,cut='roof')
    li=light('Interior_Preview_Light',(0,y,3.45),110,(1,0.80,0.54),ROOT,target=TRAIN)
    li['dr_powered'],li['dr_energy']=True,110.0
# Rear roof fighting station; open access hatch with ladder, no roof across it.
for x in (-1.58,-0.92):
    beam('Roof_Access_Ladder',(x,-5.86,1.4),(x,-5.86,4.12),0.055,'yellow',ROOT)
for i in range(9):
    beam('Ladder_Rung',(-1.58,-5.86,1.58+i*0.29),(-0.92,-5.86,1.58+i*0.29),0.045,'steel',ROOT)
# Split roof locally around the hatch; roof mesh ends above ladder opening.
# Keep a genuine opening by rebuilding the rear 1.2 m roof as separate slabs.
roof.location.y=-1.12
roof.scale.y=7.95/9.15
box('Rear_Roof_Deck',(0.70,-5.65,3.84),(2.50,1.25,0.24),'dark',ROOT,cut='roof')
box('Hatch_Outer_Rim',(-1.75,-5.65,3.84),(0.25,1.25,0.24),'yellow',ROOT,cut='roof')
for x in (-1.76,1.76):
    for y in (-6.1,-4.5):
        beam('Gunner_Rail_Post',(x,y,3.98),(x,y,4.74),0.045,'yellow',ROOT)['dr_cutaway']='roof'
    beam('Gunner_Rail',(x,-6.1,4.74),(x,-4.5,4.74),0.045,'yellow',ROOT)['dr_cutaway']='roof'
for o in list(TRAIN.objects):
    ancestor=o.parent
    while ancestor:
        if ancestor in (WEAPON,SEARCH):
            o['dr_cutaway']='roof'
            break
        ancestor=ancestor.parent

# ---------------------- 직선 / 곡선 / 분기 메시 ----------------------
# frame = (중심 x, 중심 y, 오른쪽 단위벡터 x, 오른쪽 단위벡터 y)
# 진행 방향은 (-ny, nx)입니다. 입구는 항상 원점에서 +Y를 향합니다.
def straight_frames(length, steps=1, start_y=0.0):
    return [(0.0, start_y+length*i/steps, 1.0, 0.0) for i in range(steps+1)]

def arc_frame(side, radius, angle, start_y=0.0):
    return (side*radius*(1-math.cos(angle)), start_y+radius*math.sin(angle),
            math.cos(angle), -side*math.sin(angle))

def arc_frames(side, radius, angle, steps, start_y=0.0, start_angle=0.0):
    return [arc_frame(side, radius, start_angle+(angle-start_angle)*i/steps, start_y)
            for i in range(steps+1)]

def sweep_box(builder, frames, width, bottom, top, mat, offset=0.0, factors=None):
    """경로를 따라 사각 단면을 연결해, 틈 없이 이어지는 곡선 메시를 만듭니다."""
    verts, faces = [], []
    for i,(x,y,nx,ny) in enumerate(frames):
        half = width*(factors[i] if factors else 1.0)/2
        for dx,z in ((offset-half,bottom),(offset+half,bottom),
                     (offset+half,top),(offset-half,top)):
            verts.append((x+nx*dx,y+ny*dx,z))
    faces.append((0,1,2,3))
    for i in range(len(frames)-1):
        a, b = i*4, (i+1)*4
        for k in range(4):
            j = (k+1)%4
            faces.append((a+k,b+k,b+j,a+j))
    end = (len(frames)-1)*4
    faces.append((end+3,end+2,end+1,end))
    builder.add(verts, faces, mat)

def add_rail(builder, frames, offset, lift=0.0, factors=None):
    # 직선/곡선의 단면 높이와 궤간을 동일하게 유지합니다.
    for width,bottom,top in ((0.20,0.18,0.25),(0.055,0.235,0.335),(0.13,0.32,0.38)):
        sweep_box(builder, frames, width, bottom+lift, top+lift,
                  M['steel'], offset, factors)

def add_track_clip(builder, frame, offset):
    x,y,nx,ny = frame
    yaw = math.atan2(ny,nx)
    builder.box((x+nx*offset,y+ny*offset,0.175), (0.30,0.29,0.035), M['steel'], yaw)
    for dx in (-0.105,0.105):
        builder.box((x+nx*(offset+dx),y+ny*(offset+dx),0.20),
                    (0.045,0.07,0.045), M['rust'], yaw)

def add_sleeper(builder, frame):
    x,y,nx,ny = frame
    builder.box((x,y,0.075), (2.65,0.24,0.17), M['wood'], math.atan2(ny,nx))
    for offset in (-RAIL_GAUGE/2,RAIL_GAUGE/2):
        add_track_clip(builder, frame, offset)

def track_marker(root, role, frame):
    x,y,nx,ny = frame
    target = root.users_collection[0]
    o = empty(root.name+'_'+role, (x,y,0), root, target)
    o.rotation_euler.z = math.atan2(ny,nx)
    o.empty_display_type, o.empty_display_size = 'ARROWS', 0.55
    o['connection_role'] = role
    return o

def path_points(root, name, frames):
    """Unity에서 참조할 수 있는 중심선 위의 순서 있는 빈 오브젝트입니다."""
    target = root.users_collection[0]
    p = empty(root.name+'_Path_'+name, parent=root, target=target)
    p['route_name'], p['waypoint_count'] = name, len(frames)
    distance = 0.0
    previous = None
    for i,(x,y,nx,ny) in enumerate(frames):
        if previous:
            distance += math.hypot(x-previous[0],y-previous[1])
        o = empty(root.name+'_'+name+'_Waypoint_%03d'%i, (x,y,0), p, target)
        o.rotation_euler.z = math.atan2(ny,nx)
        o.empty_display_size = 0.07
        o['path_index'], o['distance_m'] = i, distance
        previous = (x,y)
    return p

def attach_module(root, exit_marker):
    """다음 모듈의 원점과 진행 방향을 이전 모듈의 출구에 맞춥니다."""
    SCENE.view_layers[0].update()
    root.location = exit_marker.matrix_world.translation
    root.rotation_euler = exit_marker.matrix_world.to_euler()
    SCENE.view_layers[0].update()

tb = MeshBuilder()
straight = straight_frames(TRACK_LENGTH)
sweep_box(tb, straight, 3.3, -0.14, 0.03, M['ballast'])
for i in range(math.ceil(TRACK_LENGTH/SLEEPER_SPACING)):
    count = math.ceil(TRACK_LENGTH/SLEEPER_SPACING)
    add_sleeper(tb, (0,(i+0.5)*TRACK_LENGTH/count,1,0))
for offset in (-RAIL_GAUGE/2,RAIL_GAUGE/2):
    add_rail(tb, straight, offset)

TRACK_ROOT = empty('TrackSegment_8m_ROOT', (0,-TRACK_LENGTH/2,0), target=TRACK)
TRACK_ROOT['length_m'], TRACK_ROOT['rail_gauge_m'] = TRACK_LENGTH, RAIL_GAUGE
TRACK_ROOT['module_type'] = 'STRAIGHT'
TRACK_ROOT['treadmill'] = 'Follow route tangent and recycle in Unity'
TRACK_MESH = tb.build('TrackSegment_8m_Mesh', TRACK, TRACK_ROOT)
track_marker(TRACK_ROOT, 'Entry', straight[0])
track_marker(TRACK_ROOT, 'Exit', straight[-1])
path_points(TRACK_ROOT, 'Main', straight_frames(TRACK_LENGTH,16))
TRACK_EXPORTS = [('DeadRail_Track_8m',TRACK_ROOT)]

for i in range(TRACK_SEGMENTS):
    offset = i-TRACK_SEGMENTS//2
    if offset == 0:
        continue
    r = empty('TrackSegment_Instance_ROOT', (0,-TRACK_LENGTH/2+offset*TRACK_LENGTH,0), target=TRACK)
    o = bpy.data.objects.new('TrackSegment_Instance_Mesh', TRACK_MESH.data)
    TRACK.objects.link(o)
    o.parent = r

CURVE_ASSETS = {}
for index,degrees in enumerate(CURVE_ANGLES):
    for side,word in ((-1,'Left'),(1,'Right')):
        tag = str(degrees).replace('.','p')
        name = 'TrackCurve_'+word+'_'+tag+'deg'
        theta = math.radians(degrees)
        steps = max(12, math.ceil(degrees/1.5))
        frames = arc_frames(side,CURVE_RADIUS,theta,steps)
        cb = MeshBuilder()
        sweep_box(cb, frames, 3.3, -0.14, 0.03, M['ballast'])
        count = math.ceil(CURVE_RADIUS*theta/SLEEPER_SPACING)
        for i in range(count):
            add_sleeper(cb, arc_frame(side,CURVE_RADIUS,theta*(i+0.5)/count))
        for offset in (-RAIL_GAUGE/2,RAIL_GAUGE/2):
            add_rail(cb, frames, offset)
        r = empty(name+'_ROOT', (side*12,-12-max(0,index-1)*(CURVE_RADIUS+8),0), target=JUNCTIONS)
        r['module_type'], r['radius_m'] = 'CURVE', CURVE_RADIUS
        r['turn_degrees'], r['arc_length_m'] = side*degrees, CURVE_RADIUS*theta
        r['rail_gauge_m'] = RAIL_GAUGE
        cb.build(name+'_Mesh', JUNCTIONS, r)
        track_marker(r,'Entry',frames[0])
        exit_marker = track_marker(r,'Exit',frames[-1])
        path_points(r,'Main',frames)
        CURVE_ASSETS[(side,degrees)] = (r,exit_marker)
        TRACK_EXPORTS.append(('DeadRail_'+name,r))

def turnout_asset(side, location):
    word = 'Right' if side>0 else 'Left'
    name = 'TrackTurnout_'+word
    radius, theta = TURNOUT_RADIUS, math.radians(TURNOUT_ANGLE)
    end_y = TURNOUT_LEAD + radius*math.sin(theta)
    frames = arc_frames(side,radius,theta,48,TURNOUT_LEAD)
    b = MeshBuilder()
    # 두 경로가 하나의 넓어지는 도상과 긴 침목을 공유합니다.
    road = []
    for i in range(49):
        y = (end_y+0.65)*i/48
        t = math.asin(min(math.sin(theta),max(0,y-TURNOUT_LEAD)/radius))
        cx = side*radius*(1-math.cos(t))
        left, right = min(-1.65,cx-1.65), max(1.65,cx+1.65)
        road.append(((left+right)/2,y,(right-left)/3.3,0))
    # 도상용 프레임의 normal 길이를 폭 배율로 사용합니다.
    sweep_box(b, road, 3.3, -0.14, 0.03, M['ballast'])
    count = math.ceil(end_y/SLEEPER_SPACING)
    for i in range(count):
        y = (i+0.5)*end_y/count
        t = math.asin(max(0,y-TURNOUT_LEAD)/radius) if y>TURNOUT_LEAD else 0.0
        cx = side*radius*(1-math.cos(t))
        left, right = min(-1.325,cx-1.325),max(1.325,cx+1.325)
        b.box(((left+right)/2,y,0.075), (right-left,0.24,0.17), M['wood'])
        for offset in (-RAIL_GAUGE/2,RAIL_GAUGE/2):
            add_track_clip(b,(0,y,1,0),offset)
        if abs(cx)>0.35:
            for offset in (-RAIL_GAUGE/2,RAIL_GAUGE/2):
                add_track_clip(b,arc_frame(side,radius,t,TURNOUT_LEAD),offset)
    for offset in (-RAIL_GAUGE/2,RAIL_GAUGE/2):
        add_rail(b,straight_frames(end_y),offset)
    # 가늘어지는 전환 레일은 분리하여 전환 표시가 가능하도록 합니다.
    blade_angle = min(0.075,theta/3)
    branch = arc_frames(side,radius,theta,40,TURNOUT_LEAD,blade_angle)
    for offset in (-RAIL_GAUGE/2,RAIL_GAUGE/2):
        # 외형용 교차부는 3mm 높여, 겹치는 수평 면의 깜빡임을 방지합니다.
        add_rail(b,branch,offset,lift=0.003)
    frog_angle = math.acos(1-RAIL_GAUGE/(radius+RAIL_GAUGE/2))
    frog_y = TURNOUT_LEAD+radius*math.sin(frog_angle)
    b.box((side*RAIL_GAUGE/2,frog_y,0.18), (0.60,1.20,0.08), M['steel'])
    guard = straight_frames(1.65,start_y=frog_y-0.825)
    sweep_box(b,guard,0.055,0.20,0.35,M['steel'],offset=side*(RAIL_GAUGE/2+0.23))

    r = empty(name+'_ROOT',location,target=JUNCTIONS)
    r['module_type'], r['rail_gauge_m'] = 'TURNOUT', RAIL_GAUGE
    r['branch_radius_m'], r['branch_degrees'] = radius, side*TURNOUT_ANGLE
    r['straight_length_m'] = end_y
    r['branch_length_m'] = TURNOUT_LEAD+radius*theta
    r['selected_exit'] = 'Exit_Straight'
    b.build(name+'_Mesh',JUNCTIONS,r)
    track_marker(r,'Entry',(0,0,1,0))
    straight_exit = track_marker(r,'Exit_Straight',(0,end_y,1,0))
    branch_exit = track_marker(r,'Exit_Branch',frames[-1])
    path_points(r,'Straight',straight_frames(end_y,32))
    lead = straight_frames(TURNOUT_LEAD,6)
    path_points(r,'Branch',lead[:-1]+frames)

    blades = empty(name+'_SwitchBlades_Pivot',parent=r,target=JUNCTIONS)
    bb = MeshBuilder()
    bf = arc_frames(side,radius,blade_angle,10,TURNOUT_LEAD)
    factors = [0.06+0.94*i/10 for i in range(11)]
    for offset in (-RAIL_GAUGE/2,RAIL_GAUGE/2):
        add_rail(bb,bf,offset,lift=0.003,factors=factors)
    bb.build(name+'_SwitchBlades_Mesh',JUNCTIONS,blades)
    lever_base = empty(name+'_SwitchLever_ROOT',(side*2.10,1.3,0.0),r,JUNCTIONS)
    box('Switch_Lever_Base',(0,0,0.25),(0.38,0.42,0.50),'dark',lever_base,JUNCTIONS)
    lever = empty(name+'_Lever_Pivot',(0,0,0.52),lever_base,JUNCTIONS)
    beam('Switch_Lever',(0,0,0),(0,0.30,0.45),0.055,'steel',lever,JUNCTIONS)
    box('Switch_Handle',(0,0.30,0.46),(0.27,0.075,0.075),'yellow',lever,JUNCTIONS)
    box('Switch_Direction_Flag',(0,0,0.43),(0.33,0.05,0.25),'red',lever,JUNCTIONS)
    TRACK_EXPORTS.append(('DeadRail_'+name,r))
    return {'root':r,'straight':straight_exit,'branch':branch_exit,'lever':lever,
            'blades':blades,'side':side}

forward_end = -TRACK_LENGTH/2 + (TRACK_SEGMENTS-1-TRACK_SEGMENTS//2)*TRACK_LENGTH + TRACK_LENGTH
SWITCHES = {'RIGHT':turnout_asset(1,(0,forward_end,0)),
            'LEFT':turnout_asset(-1,(-21,forward_end,0))}

# 기본 분기 예시: 기차 앞 직선 -> 우측 분기기 -> 서로 다른 곡선 경로.
first_angle = CURVE_ANGLES[0]
attach_module(CURVE_ASSETS[(-1,first_angle)][0],SWITCHES['RIGHT']['straight'])
attach_module(CURVE_ASSETS[(1,first_angle)][0],SWITCHES['RIGHT']['branch'])
ROUTE_TAILS = []
for side in (-1,1):
    module, exit_marker = CURVE_ASSETS[(side,first_angle)]
    r = empty('Route_Exit_StraightPreview_ROOT',target=JUNCTIONS)
    o = bpy.data.objects.new('Route_Exit_StraightPreview_Mesh',TRACK_MESH.data)
    JUNCTIONS.objects.link(o)
    o.parent = r
    attach_module(r,exit_marker)
    ROUTE_TAILS.append(r)

def set_switch_preview(which='RIGHT', branch=False):
    """분기 선택을 외형으로 표시합니다. 실제 경로 선택은 Unity에서 구현합니다."""
    key = which.upper()
    if key not in SWITCHES:
        raise ValueError("which에는 'RIGHT' 또는 'LEFT'를 지정하세요.")
    data = SWITCHES[key]
    data['root']['selected_exit'] = 'Exit_Branch' if branch else 'Exit_Straight'
    data['lever'].rotation_euler.z = data['side']*math.radians(55) if branch else 0
    data['blades'].location.x = 0 if branch else -data['side']*0.055
    print('Turnout preview:',key,data['root']['selected_exit'])

set_switch_preview('RIGHT',False)
set_switch_preview('LEFT',False)

# ---------------------- Shootable trackside switch ----------------------
SHOT_SWITCH=empty('TrackSwitch_ROOT',(4.7,7.6,0),target=JUNCTIONS)
SHOT_SWITCH['interaction']='Shoot target to toggle LEFT / RIGHT; Unity gameplay required'
SHOT_SWITCH['selected_direction']='LEFT'
box('Switch_Concrete_Foot',(0,0,0.12),(1.6,1.0,0.24),'ballast',SHOT_SWITCH,JUNCTIONS,bevel=0.06)
box('Switch_Motor_Box',(0,0,0.47),(1.15,0.72,0.48),'dark',SHOT_SWITCH,JUNCTIONS,bevel=0.04)
for x in (-0.5,0.5):
    cylinder('Switch_Anchor_Bolt',(x,0,0.73),0.045,0.10,'steel',SHOT_SWITCH,target=JUNCTIONS)
cylinder('Switch_Mast',(0,0,1.26),0.095,1.4,'yellow',SHOT_SWITCH,target=JUNCTIONS)
SWITCH_PIVOT=empty('TrackSwitch_Lever_Pivot',(0,0,1.38),SHOT_SWITCH,JUNCTIONS)
SWITCH_PIVOT['rotation_axis']='Y; left -25 degrees, right +25 degrees'
beam('Shoot_Lever',(0,0,0),(0,0,0.90),0.10,'steel',SWITCH_PIVOT,JUNCTIONS)
TARGET=cylinder('TrackSwitch_HitTarget',(0,-0.09,0.85),0.43,0.12,'yellow',SWITCH_PIVOT,(math.pi/2,0,0),sides=16,target=JUNCTIONS)
TARGET['unity_collider']='SphereCollider radius 0.43; route hit to TrackSwitch'
cylinder('Target_Black_Ring',(0,-0.16,0.85),0.32,0.025,'dark',SWITCH_PIVOT,(math.pi/2,0,0),sides=16,target=JUNCTIONS)
cylinder('Target_Bullseye',(0,-0.18,0.85),0.20,0.025,'emergency',SWITCH_PIVOT,(math.pi/2,0,0),sides=16,target=JUNCTIONS)
box('Target_Cross_H',(0,-0.2,0.85),(0.27,0.02,0.035),'yellow',SWITCH_PIVOT,JUNCTIONS)
box('Target_Cross_V',(0,-0.2,0.85),(0.035,0.02,0.27),'yellow',SWITCH_PIVOT,JUNCTIONS)
box('Direction_Plate',(0,0.1,2.95),(1.95,0.14,0.55),'dark',SHOT_SWITCH,JUNCTIONS,bevel=0.04)
label('Left_Indicator','< LEFT',(-0.87,0.015,2.96),0.19,SHOT_SWITCH,'screen_text',target=JUNCTIONS)
label('Right_Indicator','RIGHT >',(0.1,0.015,2.96),0.19,SHOT_SWITCH,'yellow',target=JUNCTIONS)
SWITCH_LEFT=box('Left_Active_Lamp',(-0.5,0.01,2.79),(0.7,0.03,0.08),'screen',SHOT_SWITCH,JUNCTIONS)
SWITCH_RIGHT=box('Right_Active_Lamp',(0.5,0.01,2.79),(0.7,0.03,0.08),'dark',SHOT_SWITCH,JUNCTIONS)
beam('Indicator_Post',(0,0.20,0.65),(0,0.20,2.95),0.09,'steel',SHOT_SWITCH,JUNCTIONS)
beam('Switch_Actuator_Rod',(-0.5,0,0.37),(-1.85,0,0.37),0.07,'steel',SHOT_SWITCH,JUNCTIONS)
def set_shot_switch(direction='LEFT'):
    direction=direction.upper()
    if direction not in ('LEFT','RIGHT'):
        raise ValueError('Use LEFT or RIGHT')
    SHOT_SWITCH['selected_direction']=direction
    SWITCH_PIVOT.rotation_euler.y=math.radians(-25 if direction=='LEFT' else 25)
    for o,active in ((SWITCH_LEFT,direction=='LEFT'),(SWITCH_RIGHT,direction=='RIGHT')):
        o.data.materials.clear()
        o.data.materials.append(M['screen'] if active else M['dark'])
set_shot_switch('LEFT')
bpy.app.driver_namespace['deadrail_shot_switch']=set_shot_switch

# ---------------------- 프리뷰 배경 ----------------------
box('Preview_Ground', (0,20,-0.22), (88,150,0.10), 'ground', target=ENV)
for y in (-17,15):
    sig = empty('Signal_ROOT', (-2.55,y,0), target=ENV)
    cylinder('Signal_Post', (0,0,1.52), 0.065, 3.05, 'steel', sig, target=ENV)
    box('Signal_Housing', (0,0,2.93), (0.39,0.25,0.83), 'dark', sig, target=ENV)
    for z, mat in ((2.68,'emergency'),(2.94,'yellow'),(3.20,'screen')):
        cylinder('Signal_Lens', (0,-0.15,z), 0.10, 0.035, mat, sig,
                 (math.pi/2,0,0), sides=8, target=ENV)
for i, (x,y) in enumerate(((-3.5,-10),(3.6,-12),(-4,8),(3.5,13))):
    b = box('Trackside_Debris', (x,y,0.08), (0.65,1.1,0.24), 'rust', target=ENV)
    b.rotation_euler.z = 0.45*i

# ---------------------- 프리뷰 카메라 ----------------------
def camera(name, loc, aim, lens):
    d = bpy.data.cameras.new(name)
    o = bpy.data.objects.new(name,d)
    RIG.objects.link(o)
    o.location = loc
    o.rotation_euler = (Vector(aim)-Vector(loc)).to_track_quat('-Z','Y').to_euler()
    d.type, d.ortho_scale = 'ORTHO', lens
    d.clip_end = 200
    return o

CAM = camera('Camera_Cutaway', (14,-16,15), (0,0,1.7), 22)
EXTERIOR_CAM = camera('Camera_Exterior_Front', (13,19,9), (0,0,1.8), 22)
SCENE.view_layers[0].update()
# 분기 예시는 위에서 보고, 전체 모듈은 별도 카메라로 확인합니다.
fork_meshes = [CURVE_ASSETS[(side,first_angle)][0] for side in (-1,1)]
fork_meshes.append(SWITCHES['RIGHT']['root'])
fork_meshes.extend(ROUTE_TAILS)
def mesh_bounds(roots):
    """지정된 루트의 메시 경계상자를 월드 공간에서 수집합니다."""
    points = []
    def collect(o):
        if o.type == 'MESH':
            points.extend(o.matrix_world @ Vector(corner) for corner in o.bound_box)
        for child in o.children:
            collect(child)
    for root in roots:
        collect(root)
    xs,ys = [p.x for p in points],[p.y for p in points]
    return min(xs),max(xs),min(ys),max(ys)

def plan_camera(name, bounds):
    xmin,xmax,ymin,ymax = bounds
    x,y = (xmin+xmax)/2,(ymin+ymax)/2
    aspect = SCENE.render.resolution_x/SCENE.render.resolution_y
    width = max(xmax-xmin+8,(ymax-ymin+8)*aspect)
    return camera(name,(x,y,120),(x,y,0),width)

FORK_CAM = plan_camera('Camera_Route_Fork',mesh_bounds(fork_meshes))
OVERVIEW_CAM = plan_camera('Camera_All_Track_Modules',
    mesh_bounds([o for c in (TRACK,JUNCTIONS) for o in c.objects if o.parent is None]))
SCENE.camera = CAM if CUTAWAY else EXTERIOR_CAM
sun = light('Preview_Sun', (0,0,15), 2.5, (0.80,0.87,1.0), kind='SUN')
sun.rotation_euler = (math.radians(25),math.radians(-20),math.radians(-30))
area = light('Preview_Softbox', (4,-2,13), 1900, (1,0.84,0.65), kind='AREA')
area.data.shape, area.data.size = 'DISK', 12

def show_track_view(overview=False):
    if bpy.context.window:
        bpy.context.window.scene = SCENE
    SCENE.camera = OVERVIEW_CAM if overview else FORK_CAM
    if bpy.context.screen:
        for a in bpy.context.screen.areas:
            if a.type == 'VIEW_3D':
                a.spaces.active.region_3d.view_perspective = 'CAMERA'
    print('Track camera:',SCENE.camera.name)

# ---------------------- 모드 전환 / 저장 / FBX ----------------------
def set_cutaway(enabled=True):
    """생성된 부품을 숨기거나 복원합니다. 메시 자체는 삭제하지 않습니다."""
    for o in TRAIN.objects:
        if o.get('dr_cutaway'):
            o.hide_render = bool(enabled)
            # 다른 씬이 활성화되어 있어도 해당 씬의 View Layer를 지정합니다.
            o.hide_set(bool(enabled), view_layer=SCENE.view_layers[0])
    SCENE['deadrail_cutaway'] = bool(enabled)

def set_blackout(enabled=True):
    """재질/프리뷰 조명 상태만 변경합니다. 게임의 상태 머신은 Unity에서 구현합니다."""
    powered = {'screen':1.2, 'screen_text':1.5, 'lamp':2.5}
    for key, strength in powered.items():
        p = M[key].node_tree.nodes.get('Principled BSDF')
        p.inputs['Emission Strength'].default_value = 0 if enabled else strength
        normal = M[key].diffuse_color
        p.inputs['Base Color'].default_value = (0.008,0.012,0.012,1) if enabled else normal
    for o in TRAIN.objects:
        if o.type == 'LIGHT' and o.get('dr_powered'):
            o.data.energy = 0 if enabled else o['dr_energy']
    SCENE['deadrail_blackout'] = bool(enabled)

def descendants(root):
    result = [root]
    for child in root.children:
        result.extend(descendants(child))
    return result

def output_folder():
    base = Path(OUTPUT_DIR).expanduser() if OUTPUT_DIR else Path.home()/'DeadRail_Output'
    folder = base/datetime.now().strftime('%Y%m%d_%H%M%S_%f')
    folder.mkdir(parents=True, exist_ok=False)
    return folder

def export_assets(folder=None):
    """단면/암전 프리뷰를 복원한 상태로 전체 모델을 내보낸 뒤 프리뷰를 돌려놓습니다."""
    folder = Path(folder) if folder else output_folder()
    folder.mkdir(parents=True, exist_ok=True)
    if bpy.context.window:
        bpy.context.window.scene = SCENE
    vl = SCENE.view_layers[0]
    cuts = SCENE.get('deadrail_cutaway', False)
    black = SCENE.get('deadrail_blackout', False)
    selected = [o for o in SCENE.objects if o.select_get(view_layer=vl)]
    active = vl.objects.active
    hidden_states = [(o,o.hide_get(view_layer=vl)) for o in SCENE.objects]
    for o,hidden in hidden_states:
        o.hide_set(False,view_layer=vl)
    set_cutaway(False)
    set_blackout(False)
    assets = [('DeadRail_Train',ROOT)] + TRACK_EXPORTS + [
              ('DeadRail_P2_Camera',P2_CAMERA),('DeadRail_Diesel_Engine',ENGINE),('DeadRail_TrackSwitch',SHOT_SWITCH),
              ('DeadRail_Battery_30kg',BATTERY),('DeadRail_Manual_Generator',GENERATOR),
              ('DeadRail_Drive_Console',DRIVE),('DeadRail_Radar_Console',RADAR),
              ('DeadRail_Search_Control',SEARCH_CONTROL),('DeadRail_Searchlight',SEARCH),
              ('DeadRail_Weapon_Mount',WEAPON)]
    try:
        with bpy.context.temp_override(scene=SCENE, view_layer=vl):
            for filename, root in assets:
                for o in SCENE.objects:
                    o.select_set(False, view_layer=vl)
                old_loc, old_parent = root.location.copy(), root.parent
                try:
                    # 개별 에셋의 루트는 원점에 놓고, 자식의 로컬 위치는 유지합니다.
                    root.parent, root.location = None, (0,0,0)
                    for o in descendants(root):
                        if o.type in {'EMPTY','MESH','FONT'}:
                            o.select_set(True, view_layer=vl)
                    vl.objects.active = root
                    vl.update()
                    bpy.ops.export_scene.fbx(filepath=str(folder/(filename+'.fbx')),
                        check_existing=False, use_selection=True, use_visible=False,
                        object_types={'EMPTY','MESH','OTHER'}, use_mesh_modifiers=True,
                        use_triangles=True, bake_anim=False, add_leaf_bones=False,
                        axis_forward='-Z', axis_up='Y', global_scale=1.0,
                        apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
                        use_custom_props=True)
                finally:
                    root.parent, root.location = old_parent, old_loc
                    vl.update()
    finally:
        set_cutaway(cuts)
        set_blackout(black)
        for o in SCENE.objects:
            o.select_set(False, view_layer=vl)
        for o in selected:
            o.select_set(True, view_layer=vl)
        vl.objects.active = active
        for o,hidden in hidden_states:
            o.hide_set(hidden,view_layer=vl)
    print('Dead Rail FBX saved:', folder)
    return str(folder)

# This block is embedded in the standalone v2 generator (after base helpers).
BLACKOUT_ASSETS = collection('DR_06_Blackout_Tunnel_Assets')
M['concrete'] = material('Tunnel_Concrete', (0.24,0.28,0.29))
M['stone'] = material('Portal_Stone', (0.38,0.40,0.37))
M['timber'] = material('Barricade_Timber', (0.28,0.13,0.045))
M['endgrain'] = material('Broken_Endgrain', (0.56,0.32,0.12))

# P2 camera shares the light's yaw and pitch. The empty is an engine socket.
P2_CAMERA = empty('P2_InspectionCamera_ROOT',(-0.58,0,0),PITCH)
box('Camera_Arm',(0.28,0,-0.12),(0.58,0.09,0.09),'steel',P2_CAMERA)
box('Camera_Weatherproof_Body',(0,0,0),(0.33,0.50,0.28),'body',P2_CAMERA,bevel=0.035)
box('Camera_Rain_Hood',(0,0.06,0.18),(0.40,0.64,0.045),'dark',P2_CAMERA)
cylinder('Camera_Lens_Barrel',(0,0.30,0),0.115,0.17,'dark',P2_CAMERA,(-math.pi/2,0,0),sides=16)
cylinder('Camera_Optical_Glass',(0,0.39,0),0.085,0.015,'blue',P2_CAMERA,(-math.pi/2,0,0),sides=16)
P2_SOCKET=empty('P2_Camera_ViewSocket',(0,0.405,0),P2_CAMERA)
P2_SOCKET['forward']='Blender +Y; Unity Camera local forward must be verified after FBX import'
P2_SOCKET['unity_setup']='Camera -> RenderTexture 1024x576 -> P2 console screen; shares searchlight pivots'
for o in descendants(P2_CAMERA):
    o['dr_cutaway']='roof'
for o in descendants(SEARCH_CONTROL):
    if o.name.startswith('CRT_Screen'):
        o.name='P2_CameraFeed_Screen'
        o['unity_material']='Unlit RenderTexture feed; keep UI text on separate canvas'
    if o.type=='FONT' and o.name.startswith('CRT_Status'):
        o.data.body='CAM 02 / LIVE'

# Replace low partial rails with a closed 1.15 m roof cage, midrail and toe boards.
for o in list(TRAIN.objects):
    if o.name.startswith(('Gunner_Rail','Gunner_Rail_Post')):
        bpy.data.objects.remove(o,do_unlink=True)
GUARD=empty('P3_Roof_SafetyFence_ROOT',parent=ROOT)
GUARD['unity_collider']='Four continuous BoxColliders around perimeter, 1.15m high; gate closed during play'
for x in (-1.77,1.77):
    for y in (-6.16,-5.34,-4.48):
        beam('Fence_Post',(x,y,3.97),(x,y,5.12),0.065,'yellow',GUARD)
    for z in (4.08,4.57,5.12):
        beam('Fence_SideRail',(x,-6.16,z),(x,-4.48,z),0.065,'yellow',GUARD)
    box('Fence_Toeboard',(x,-5.32,4.06),(0.06,1.68,0.18),'dark',GUARD)
for y in (-6.16,-4.48):
    for x in (-0.58,0.58):
        beam('Fence_EndPost',(x,y,3.97),(x,y,5.12),0.065,'yellow',GUARD)
    for z in (4.08,4.57,5.12):
        beam('Fence_EndRail',(-1.77,y,z),(1.77,y,z),0.065,'yellow',GUARD)
    box('Fence_EndToeboard',(0,y,4.06),(3.54,0.06,0.18),'dark',GUARD)
# Protect roof hatch edge; openable gate has a local hinge for Unity.
for y in (-6.08,-5.06):
    beam('Hatch_Guard',(-0.59,y,3.98),(-0.59,y,5.08),0.05,'yellow',GUARD)
HATCH_GATE=empty('P3_Hatch_SafetyGate_Pivot',(-0.59,-6.08,4.0),GUARD)
for z in (0.55,1.08):
    beam('Hatch_GateRail',(0,0,z),(0,1.02,z),0.045,'yellow',HATCH_GATE)
beam('Hatch_GateEnd',(0,1.02,0),(0,1.02,1.08),0.045,'yellow',HATCH_GATE)
HATCH_GATE['open_angle_degrees']=90.0
for o in descendants(GUARD): o['dr_cutaway']='roof'

# Keep legacy modules available in a hidden library, not in the three-way showcase.
LEGACY=collection('DR_99_Legacy_TwoWay_Library')
for o in list(JUNCTIONS.objects):
    if o not in descendants(SHOT_SWITCH):
        JUNCTIONS.objects.unlink(o)
        LEGACY.objects.link(o)
LEGACY.hide_render=True
LEGACY.hide_viewport=True
TRACK_EXPORTS = [('DeadRail_Track_8m',TRACK_ROOT)]

THREEWAY=empty('Track_ThreeWay_ROOT',(0,forward_end,0),target=JUNCTIONS)
THREEWAY['module_type']='THREE_WAY'
THREEWAY['routes']='LEFT, STRAIGHT, RIGHT'
THREEWAY['selected_exit']='Exit_Straight'
THREEWAY['note']='Simplified game rail geometry, not a railway engineering simulation'
radius,theta,lead=36.0,math.radians(25),3.0
end_y=lead+radius*math.sin(theta)
fork=MeshBuilder()
# Shared widening ballast and sleepers.
bed=[]
for i in range(49):
    y=end_y*i/48
    t=math.asin(max(0,y-lead)/radius)
    half=1.65+radius*(1-math.cos(t))
    bed.append((0,y,half/1.65,0))
sweep_box(fork,bed,3.3,-0.14,0.03,M['ballast'])
for i in range(math.ceil(end_y/SLEEPER_SPACING)):
    y=(i+0.5)*end_y/math.ceil(end_y/SLEEPER_SPACING)
    t=math.asin(max(0,y-lead)/radius)
    width=2.65+2*radius*(1-math.cos(t))
    fork.box((0,y,0.075),(width,0.24,0.17),M['wood'])
straight=straight_frames(end_y,48)
for offset in (-RAIL_GAUGE/2,RAIL_GAUGE/2): add_rail(fork,straight,offset)
track_marker(THREEWAY,'Entry',(0,0,1,0))
THREE_EXITS={'STRAIGHT':track_marker(THREEWAY,'Exit_Straight',straight[-1])}
path_points(THREEWAY,'Straight',straight)
for side,word in ((-1,'Left'),(1,'Right')):
    frames=arc_frames(side,radius,theta,48,lead)
    for offset in (-RAIL_GAUGE/2,RAIL_GAUGE/2):
        add_rail(fork,frames,offset,lift=0.003)
    THREE_EXITS[word.upper()]=track_marker(THREEWAY,'Exit_'+word,frames[-1])
    path_points(THREEWAY,word,straight_frames(lead,6)[:-1]+frames)
fork.build('ThreeWay_RailBed_Mesh',JUNCTIONS,THREEWAY)
TRACK_EXPORTS.append(('DeadRail_Track_ThreeWay',THREEWAY))
# Straight extension lengths reach the tunnel and beyond. Side routes visibly bypass it.
for word,marker in THREE_EXITS.items():
    length=44.0
    frames=straight_frames(length,44)
    b=MeshBuilder()
    sweep_box(b,frames,3.3,-0.14,0.03,M['ballast'])
    for offset in (-RAIL_GAUGE/2,RAIL_GAUGE/2): add_rail(b,frames,offset)
    for i in range(66): add_sleeper(b,(0,(i+0.5)*length/66,1,0))
    r=empty('ThreeWay_'+word+'_Continuation_ROOT',target=JUNCTIONS)
    b.build('Continuation_'+word,JUNCTIONS,r)
    attach_module(r,marker)

# Upgrade shootable prop to 3 discrete states, including straight / blackout tunnel.
for o in list(descendants(SHOT_SWITCH)):
    if o.name.startswith(('Direction_Plate','Left_Indicator','Right_Indicator','Left_Active','Right_Active')):
        bpy.data.objects.remove(o,do_unlink=True)
box('ThreeWay_Direction_Plate',(0,0.1,3.0),(3.05,0.15,0.62),'dark',SHOT_SWITCH,JUNCTIONS,bevel=0.04)
THREE_LAMPS={}
for x,word in ((-1.0,'LEFT'),(0,'STRAIGHT'),(1.0,'RIGHT')):
    label('Route_'+word,word,(x-0.41,0.015,3.04),0.15,SHOT_SWITCH,'yellow',target=JUNCTIONS)
    THREE_LAMPS[word]=box('Route_Lamp_'+word,(x,0,2.86),(0.74,0.03,0.09),'dark',SHOT_SWITCH,JUNCTIONS)
SHOT_SWITCH['interaction']='Shot cycles LEFT -> STRAIGHT -> RIGHT; Unity gameplay integration required'
SWITCH_PIVOT['rotation_axis']='Blender Y: LEFT -25 / STRAIGHT 0 / RIGHT +25 degrees'
def set_shot_switch(direction='STRAIGHT'):
    direction=direction.upper()
    if direction not in THREE_LAMPS: raise ValueError('Use LEFT, STRAIGHT or RIGHT')
    SHOT_SWITCH['selected_direction']=direction
    THREEWAY['selected_exit']='Exit_'+direction.title()
    SWITCH_PIVOT.rotation_euler.y=math.radians({'LEFT':-25,'STRAIGHT':0,'RIGHT':25}[direction])
    for word,o in THREE_LAMPS.items():
        o.data.materials.clear()
        o.data.materials.append(M['screen'] if word==direction else M['dark'])
set_shot_switch()
bpy.app.driver_namespace['deadrail_shot_switch']=set_shot_switch

# Tunnel: 9.6m clear width, 7.8m crown, 24m long; hollow all the way through.
TUNNEL=empty('Blackout_Tunnel_ROOT',(0,60,0),target=BLACKOUT_ASSETS)
TUNNEL['clear_width_m']=9.6
TUNNEL['crown_height_m']=7.8
TUNNEL['length_m']=24.0
def arch_segment(name,y,depth,inner,outer,parent,mat='concrete'):
    b=MeshBuilder()
    for i in range(20):
        a,c=math.pi*i/20,math.pi*(i+1)/20
        v=[(r*math.cos(t),dy,3+r*math.sin(t)) for dy in (y,y+depth) for r,t in ((inner,a),(outer,a),(outer,c),(inner,c))]
        b.add(v,[(3,2,1,0),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],M[mat])
    return b.build(name,BLACKOUT_ASSETS,parent)
for i in range(6):
    arch_segment('Tunnel_Vault_4m',i*4,4,4.8,5.35,TUNNEL)
    for x in (-5.075,5.075):
        box('Tunnel_Sidewall',(x,i*4+2,1.5),(0.55,4,3),'concrete',TUNNEL,BLACKOUT_ASSETS)
for y in (0,4,8,12,16,20,24):
    arch_segment('Tunnel_Rib',y-0.13,0.26,4.65,4.80,TUNNEL,'steel')
    for x in (-4.73,4.73):
        box('Wall_Rib',(x,y,1.5),(0.15,0.26,3),'steel',TUNNEL,BLACKOUT_ASSETS)
for y in (-0.6,23.7):
    arch_segment('Stone_Portal_Arch',y,0.9,4.8,5.7,TUNNEL,'stone')
    for x in (-5.25,5.25):
        box('Portal_Pier',(x,y+0.45,1.5),(0.9,0.9,3),'stone',TUNNEL,BLACKOUT_ASSETS)
        box('Portal_Base',(x,y+0.45,0.2),(1.25,1.3,0.4),'dark',TUNNEL,BLACKOUT_ASSETS)
    for i in range(21):
        t=math.pi*i/20
        beam('Arch_Stone_Joint',(4.83*math.cos(t),y-0.015,3+4.83*math.sin(t)),(5.68*math.cos(t),y-0.015,3+5.68*math.sin(t)),0.035,'dark',TUNNEL,BLACKOUT_ASSETS)
for side in (-1,1):
    box('Tunnel_Service_Walkway',(side*3.8,12,0.12),(1.8,24,0.24),'floor',TUNNEL,BLACKOUT_ASSETS)
    beam('Tunnel_Power_Conduit',(side*4.55,0,2.6),(side*4.55,24,2.6),0.07,'dark',TUNNEL,BLACKOUT_ASSETS)
    for y in (2,8,14,20):
        box('Tunnel_Emergency_Lamp',(side*4.49,y,2.85),(0.20,0.28,0.45),'emergency',TUNNEL,BLACKOUT_ASSETS,bevel=0.04)
        light('Tunnel_Red_Practical',(side*4.1,y,2.8),55,(1,0.16,0.035),TUNNEL,target=BLACKOUT_ASSETS)
for x in (-6.1,6.1):
    box('Portal_Wingwall',(x,-0.1,2.1),(1.0,3.0,4.2),'concrete',TUNNEL,BLACKOUT_ASSETS)
box('Tunnel_ID_Plate',(0,-0.63,8.4),(3.8,0.12,0.48),'dark',TUNNEL,BLACKOUT_ASSETS)
label('Tunnel_ID','DARK NODE / 04',(-1.55,-0.705,8.28),0.26,TUNNEL,'yellow',target=BLACKOUT_ASSETS)
empty('Tunnel_Entry',(0,0,0),TUNNEL,BLACKOUT_ASSETS)
empty('Tunnel_Exit',(0,24,0),TUNNEL,BLACKOUT_ASSETS)

def timber_fragment(name,a,b,width,parent,broken=False):
    a,b=Vector(a),Vector(b)
    delta=b-a
    mb=MeshBuilder()
    if not broken:
        o=beam(name,a,b,width,'timber',parent,BLACKOUT_ASSETS)
        # Beams have intentionally different depth from their width.
        o.scale.y=0.45
        return o
    w,d,L=width/2,width*0.22,delta.length
    # Serrated end silhouette, with exposed endgrain on the broken cap.
    profile=[(-w,-L/2),(w,-L/2),(w,L/2-0.12),(w*0.5,L/2+0.12),(0,L/2-0.08),(-w*0.5,L/2+0.06),(-w,L/2-0.18)]
    n=len(profile)
    verts=[(x,y,z) for y in (-d,d) for x,z in profile]
    mb.add(verts,[tuple(reversed(range(n))),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)],M['timber'])
    # Assign visibly pale material to serrated edge faces.
    o=mb.build(name,BLACKOUT_ASSETS,parent,(a+b)/2)
    o.data.materials.append(M['endgrain'])
    for poly in list(o.data.polygons)[4:8]: poly.material_index=1
    o.rotation_euler=delta.to_track_quat('Z','Y').to_euler()
    o['unity_rigidbody']='Add Rigidbody + BoxCollider; activate at impact; disable after settling'
    return o

def board_barrier(name,y):
    intact=empty(name+'_Intact_ROOT',(0,y,0),target=BLACKOUT_ASSETS)
    debris=empty(name+'_Debris_ROOT',(0,y,0),target=BLACKOUT_ASSETS)
    for x in (-4.2,4.2):
        box('Timber_Anchor_Post',(x,0,3.4),(0.28,0.26,6.5),'timber',intact,BLACKOUT_ASSETS)
    boards=[((-4.4,0,1.25),(4.4,0,1.25),0.48),((-4.4,0.12,2.9),(4.4,0.12,2.9),0.5),((-4.4,0,4.5),(4.4,0,4.5),0.5),((-3.6,0.1,6.1),(3.6,0.1,6.1),0.5),((-4.3,-0.15,1.7),(3.6,-0.15,6.5),0.55),((-3.6,-0.32,6.5),(4.3,-0.32,1.7),0.55)]
    for i,(a,b,w) in enumerate(boards):
        timber_fragment('Barricade_Plank_%02d'%i,a,b,w,intact)
        av,bv=Vector(a),Vector(b)
        mid=av.lerp(bv,0.47)
        for j,(v1,v2) in enumerate(((av,mid),(bv,mid))):
            o=timber_fragment('Wood_Splinter_%02d_%d'%(i,j),v1,v2,w,debris,True)
            o['piece_id']=i*2+j
    for x in (-4.15,4.15):
        for z in (1.25,2.9,4.5):
            cylinder('Board_Bolt',(x,-0.15,z),0.055,0.05,'steel',intact,(math.pi/2,0,0),target=BLACKOUT_ASSETS)
    intact['state']='Intact: disable on impact'
    debris['state']='Broken: enable on impact; pieces begin at original board locations'
    for o in descendants(debris):
        o.hide_render=True
        o.hide_set(True)
    return intact,debris
ENTRY_BOARDS,ENTRY_DEBRIS=board_barrier('Tunnel_Entry_Boards',59.1)
EXIT_BOARDS,EXIT_DEBRIS=board_barrier('Tunnel_Exit_Boards',84.9)

def roadblock(name,y):
    r=empty(name,(0,y,0),target=BLACKOUT_ASSETS)
    for x in (-1.75,1.75):
        for dy in (-0.55,0.55):
            beam('Barrier_A_Frame',(x,dy,0.1),(x,0,1.65),0.11,'yellow',r,BLACKOUT_ASSETS)
    box('Barrier_Hazard_Board',(0,0,1.45),(4.4,0.18,0.70),'yellow',r,BLACKOUT_ASSETS)
    for x in (-1.8,-1.2,-0.6,0,0.6,1.2,1.8):
        o=box('Barrier_Black_Stripe',(x,-0.102,1.45),(0.24,0.02,0.63),'dark',r,BLACKOUT_ASSETS)
        o.rotation_euler.y=math.radians(-24)
    for x in (-1.65,-0.55,0.55,1.65):
        cylinder('Warning_Beacon_Base',(x,0,1.87),0.12,0.13,'dark',r,target=BLACKOUT_ASSETS)
        cylinder('Warning_Red_Beacon',(x,0,2.03),0.105,0.22,'emergency',r,target=BLACKOUT_ASSETS)
    r['interaction']='Separate movable obstacle; hide or animate away at impact'
    return r
ENTRY_BLOCK=roadblock('Tunnel_Approach_Roadblock_ROOT',55.8)
EXIT_BLOCK=roadblock('Tunnel_Exit_Roadblock_ROOT',85.8)
for x,y in ((-3.1,54.4),(3.2,56.5),(-3.4,85.7)):
    box('Concrete_Rubble',(x,y,0.4),(0.85,1.0,0.8),'stone',target=BLACKOUT_ASSETS,bevel=0.15)
    for z in (0.1,0.8):
        box('Rubble_Rebar',(x,y,z),(1.1,0.055,0.055),'rust',target=BLACKOUT_ASSETS)

BLACKOUT_EXPORTS=[('DeadRail_Blackout_Tunnel',TUNNEL),('DeadRail_Tunnel_Entry_Boards',ENTRY_BOARDS),('DeadRail_Tunnel_Entry_Debris',ENTRY_DEBRIS),('DeadRail_Tunnel_Exit_Boards',EXIT_BOARDS),('DeadRail_Tunnel_Exit_Debris',EXIT_DEBRIS),('DeadRail_Approach_Roadblock',ENTRY_BLOCK),('DeadRail_Exit_Roadblock',EXIT_BLOCK)]
TRACK_EXPORTS.extend(BLACKOUT_EXPORTS)
def set_barricade_preview(broken=False):
    for intact,debris in ((ENTRY_BOARDS,ENTRY_DEBRIS),(EXIT_BOARDS,EXIT_DEBRIS)):
        for o in descendants(intact): o.hide_render=bool(broken); o.hide_set(bool(broken))
        for o in descendants(debris): o.hide_render=not broken; o.hide_set(not broken)
bpy.app.driver_namespace['deadrail_barricade']=set_barricade_preview

FORK_CAM=camera('Camera_ThreeWay_Overview',(36,6,54),(0,52,0),76)
TUNNEL_CAM=camera('Camera_Tunnel_Entrance',(15,38,13),(0,62,3.4),27)
ROOF_CAM=camera('Camera_P2_P3_Details',(10,-13,12),(0,-0.4,4.1),18)
P2_CAM=camera('Camera_P2_Closeup',(5,9,6.8),(0.8,4.9,4.55),3.8)
TUNNEL_EXIT_CAM=camera('Camera_Tunnel_Exit',(12,99,11),(0,83,3.2),23)
SCENE['v2_notes']='P2 camera, closed P3 roof fence, 3-way track, blackout tunnel and breakable boards. Static assets; Unity integration required.'


set_cutaway(CUTAWAY)
set_blackout(BLACKOUT)
SCENE['deadrail_notes'] = 'Static art assets only. Origin-fixed train, recycled track modules.'
bpy.app.driver_namespace['deadrail_cutaway'] = set_cutaway
bpy.app.driver_namespace['deadrail_blackout'] = set_blackout
bpy.app.driver_namespace['deadrail_export'] = export_assets
bpy.app.driver_namespace['deadrail_track_view'] = show_track_view
bpy.app.driver_namespace['deadrail_switch_preview'] = set_switch_preview
if START_IN_TRACK_VIEW:
    show_track_view()

# 안내문은 .blend 안의 텍스트로도 보관합니다.
notes = bpy.data.texts.new('DeadRail_README')
notes.write(__doc__ or '')

if bpy.context.screen:
    for a in bpy.context.screen.areas:
        if a.type == 'VIEW_3D':
            space = a.spaces.active
            space.shading.type = 'MATERIAL'
            space.region_3d.view_perspective = 'CAMERA'
            space.overlay.show_extras = False

if EXPORT_FBX or SAVE_BLEND:
    saved_folder = output_folder()
    if EXPORT_FBX:
        export_assets(saved_folder)
    if SAVE_BLEND:
        bpy.ops.wm.save_as_mainfile(filepath=str(saved_folder/'DeadRail_LowPoly.blend'), check_existing=False)
        print('Dead Rail blend saved:', saved_folder)

triangles = sum(sum(max(0,len(p.vertices)-2) for p in o.data.polygons)
                for o in TRAIN.objects if o.type == 'MESH')
print('Dead Rail generated:', SCENE.name)
print('Train base mesh triangles (before bevel/font conversion):', triangles)
print('Use Ctrl+S to save the current Blender project. See text: DeadRail_README.')
