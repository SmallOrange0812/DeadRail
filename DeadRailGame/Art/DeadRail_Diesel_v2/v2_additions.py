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
