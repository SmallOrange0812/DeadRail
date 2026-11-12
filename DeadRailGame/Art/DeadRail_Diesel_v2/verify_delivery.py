from pathlib import Path
import bpy
import json
out=Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(out/'DeadRail_Diesel_v2.blend'))
assert bpy.data.objects.get('Track_ThreeWay_ROOT')
assert bpy.data.objects.get('P2_Camera_ViewSocket')
assert bpy.data.objects.get('P3_Hatch_SafetyGate_Pivot')
assert len(bpy.data.texts['Unity_Cutscene_Guide_KO.md'].as_string())>1000
results={}
for file,expected in [('DeadRail_Tunnel_Entry_Debris.fbx',12),('DeadRail_Tunnel_Exit_Debris.fbx',12)]:
    old=set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(out/'FBX'/file))
    added=set(bpy.data.objects)-old
    count=sum(o.type=='MESH' for o in added)
    assert count==expected,(file,count)
    results[file]=count
old=set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=str(out/'FBX'/'DeadRail_Track_ThreeWay.fbx'))
added=set(bpy.data.objects)-old
assert all(any(('Exit_'+word) in o.name for o in added) for word in ('Left','Straight','Right'))
results['three_exit_markers_imported']=True
(out/'fbx_roundtrip_validation.json').write_text(json.dumps(results,indent=2),encoding='utf-8')
print('BLEND REOPEN AND FBX ROUNDTRIP PASSED',results)
