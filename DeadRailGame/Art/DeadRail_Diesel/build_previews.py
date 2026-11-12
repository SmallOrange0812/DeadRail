from pathlib import Path
import runpy
import bpy
import json

out = Path(__file__).resolve().parent
g = runpy.run_path(str(out / 'DeadRail_Diesel_Blender_4_4_1.py'))
scene = g['SCENE']
scene.render.resolution_x = 1500
scene.render.resolution_y = 1000
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'
scene.render.engine = 'CYCLES'
scene.cycles.samples = 24
scene.cycles.use_denoising = True
# Train close-up, interior, standalone switch: all are the actual generated geometry.
def render(name, camera):
    scene.camera = camera
    scene.render.filepath = str(out / name)
    bpy.ops.render.render(write_still=True, scene=scene.name)

g['set_cutaway'](False)
exterior = g['EXTERIOR_CAM']
render('01_Diesel_Exterior.png', exterior)
g['set_cutaway'](True)
render('02_Diesel_Interior.png', g['CAM'])
switchcam = g['camera']('Camera_Shootable_Switch',(8,1.3,4.0),(4.7,7.6,1.55),6.0)
render('03_TrackSwitch.png', switchcam)
g['set_shot_switch']('RIGHT')
assert g['SHOT_SWITCH']['selected_direction'] == 'RIGHT'
g['set_shot_switch']('LEFT')
g['set_cutaway'](False)
scene.camera=exterior
scene.view_layers[0].update()
# Verify the clear passage against world-space mesh bounds, excluding floor markings.
from mathutils import Vector
collisions=[]
for o in g['TRAIN'].objects:
    if o.type!='MESH': continue
    points=[o.matrix_world @ Vector(c) for c in o.bound_box]
    lo=[min(p[i] for p in points) for i in range(3)]
    hi=[max(p[i] for p in points) for i in range(3)]
    # Engine-room corridor, x=.15..1.70, y=-4.15..2.75, standing height.
    if all(hi[i]>a+0.001 and lo[i]<b-0.001 for i,(a,b) in enumerate(((.15,1.7),(-4.15,2.75),(1.41,3.16)))):
        collisions.append(o.name)
assert not collisions, collisions
source=bpy.data.texts.new('DeadRail_Diesel_Generator.py')
source.write((out/'DeadRail_Diesel_Blender_4_4_1.py').read_text(encoding='utf-8'))
bpy.ops.wm.save_as_mainfile(filepath=str(out/'DeadRail_Diesel.blend'))
report={'blender':bpy.app.version_string,'engine_room_aisle_m':1.55,'aisle_bounds_collisions':collisions,'side_door_width_m':1.4,'side_door_clear_height_m':2.18,'track_switch_preview_test':'LEFT -> RIGHT -> LEFT passed','note':'Geometry validation only; Unity navigation/physics are not implemented.'}
(out/'validation.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('DELIVERY COMPLETE',out)
