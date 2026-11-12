from pathlib import Path
import runpy
import json
import bpy
from mathutils import Vector

out=Path(__file__).resolve().parent
source=out/'DeadRail_Diesel_v2_Blender_4_4_1.py'
# Assemble once from the prior generator plus editable extension, producing a standalone script.
base=(out.parent/'DeadRail_Diesel'/'DeadRail_Diesel_Blender_4_4_1.py').read_text(encoding='utf-8')
addon=(out/'v2_additions.py').read_text(encoding='utf-8')
base=base.replace("set_cutaway(CUTAWAY)\n",addon+"\n\nset_cutaway(CUTAWAY)\n")
base=base.replace("('DeadRail_Diesel_Engine',ENGINE)","('DeadRail_P2_Camera',P2_CAMERA),('DeadRail_Diesel_Engine',ENGINE)")
# Select hidden debris for export, then restore the exact viewport state.
base=base.replace("active = vl.objects.active\n", "active = vl.objects.active\n    hidden_states = [(o,o.hide_get(view_layer=vl)) for o in SCENE.objects]\n    for o,hidden in hidden_states:\n        o.hide_set(False,view_layer=vl)\n")
base=base.replace("vl.objects.active = active\n", "vl.objects.active = active\n        for o,hidden in hidden_states:\n            o.hide_set(hidden,view_layer=vl)\n")
base=base.replace('DeadRail diesel locomotive / Blender 4.4.1.', 'DeadRail v2 / Blender 4.4.1. P2 camera, P3 roof cage, three-way route and blackout tunnel.')
base=base.replace('LEFT/RIGHT indicators.', 'LEFT/STRAIGHT/RIGHT indicators. Hollow tunnel with separate barricades and splinter meshes.')
source.write_text(base,encoding='utf-8')
g=runpy.run_path(str(source))
s=g['SCENE']
s.render.engine='CYCLES'
s.cycles.samples=20
s.cycles.use_denoising=True
s.render.resolution_x=1500
s.render.resolution_y=1000
s.render.resolution_percentage=100
s.render.image_settings.file_format='PNG'
# Brighter neutral presentation lights for inspectable shapes.
s.world.node_tree.nodes['Background'].inputs['Strength'].default_value=0.6
for name,cam in [('01_Train_Upgrade.png',g['EXTERIOR_CAM']),('02_P2_Camera.png',g['P2_CAM']),('03_Roof_Safety.png',g['ROOF_CAM']),('04_ThreeWay_Tunnel.png',g['FORK_CAM']),('05_Tunnel_Entrance.png',g['TUNNEL_CAM'])]:
    s.camera=cam
    s.render.filepath=str(out/name)
    bpy.ops.render.render(write_still=True,scene=s.name)

# Verify three exits and discrete state preview.
for direction in ('LEFT','STRAIGHT','RIGHT'):
    g['set_shot_switch'](direction)
    assert g['SHOT_SWITCH']['selected_direction']==direction
    assert g['THREEWAY']['selected_exit']=='Exit_'+direction.title()
g['set_shot_switch']('STRAIGHT')
assert set(g['THREE_EXITS'])=={'LEFT','STRAIGHT','RIGHT'}
assert g['P2_SOCKET'].parent.parent==g['PITCH']
assert len([o for o in g['descendants'](g['ENTRY_DEBRIS']) if o.type=='MESH'])==12
s.view_layers[0].update()
# Preserve previous standing-height engine-room aisle clearance.
hits=[]
for o in g['TRAIN'].objects:
    if o.type!='MESH':continue
    points=[o.matrix_world@Vector(c) for c in o.bound_box]
    if all(max(p[i] for p in points)>a+0.001 and min(p[i] for p in points)<b-0.001 for i,(a,b) in enumerate(((.15,1.7),(-4.15,2.75),(1.41,3.16)))):hits.append(o.name)
assert not hits,hits
# Export directly usable FBX assets outside Unity Assets, without touching existing scenes/code.
g['export_assets'](out/'FBX')
s.camera=g['EXTERIOR_CAM']
text=bpy.data.texts.new('DeadRail_v2_Generator.py')
text.write(source.read_text(encoding='utf-8'))
notes=bpy.data.texts.new('Unity_Cutscene_Guide_KO.md')
guide=out/'Unity_Cutscene_Guide_KO.md'
if guide.exists():notes.write(guide.read_text(encoding='utf-8'))
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(out/'DeadRail_Diesel_v2.blend'))
(out/'validation.json').write_text(json.dumps({'blender':bpy.app.version_string,'routes':['LEFT','STRAIGHT','RIGHT'],'switch_states_test':'passed','P2_camera_shares_pitch':True,'entry_debris_meshes':12,'engine_room_aisle_collisions':hits,'tunnel_clear_width_m':9.6,'tunnel_crown_m':7.8,'roof_fence_height_m':1.15,'fbx_files':len(list((out/'FBX').glob('*.fbx'))),'limitations':'Static meshes and preview helpers; Unity collision, camera feed, network and cutscene behavior need integration.'},indent=2),encoding='utf-8')
print('V2 DELIVERY COMPLETE')
