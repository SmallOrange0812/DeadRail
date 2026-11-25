using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

public sealed partial class RailRouteLoop
{
    private const int MaxZombies = 8;
    private const int ZombieHealth = 100;
    private const float ZombieRadius = 0.32f;

    [SerializeField] private GameObject riflePrefab;
    [SerializeField] private GameObject pistolPrefab;
    [SerializeField] private GameObject zombiePrefab;
    [Networked] private int EncounterVisit { get; set; }
    [Networked] private int ZombieCount { get; set; }
    [Networked, Capacity(MaxZombies)] private NetworkArray<ZombieState> Zombies => default;

    public struct ZombieState : INetworkStruct
    {
        public Vector3 Position;
        public float Yaw;
        public int Health;
        public int Animation;
        public TickTimer AttackCooldown;
    }

    private sealed class ZombieView
    {
        public GameObject Root;
        public PlayableGraph Graph;
        public AnimationPlayableOutput Output;
        public AnimationClipPlayable Clip;
        public Transform HeadBone;
        public SphereCollider Head;
        public CapsuleCollider Body;
        public int Animation = -1;
    }

    private static readonly string[] ZombieAnimations = { "Z_Idle", "Z_Walk_InPlace", "Z_Attack", "Z_FallingBack" };
    private readonly ZombieView[] zombieViews = new ZombieView[MaxZombies];
    private AnimationClip[] zombieClips;
    private readonly Dictionary<Material, Material> combatMaterials = new Dictionary<Material, Material>();
    private int renderedEncounter;

    internal GameObject GetPersonalWeapon(bool rifle) => rifle ? riflePrefab : pistolPrefab;

    internal void ApplyCombatMaterials(GameObject model)
    {
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null) return;
        foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material source = materials[i];
                if (source == null || source.shader.name != "Standard") continue;
                if (!combatMaterials.TryGetValue(source, out Material converted))
                {
                    converted = new Material(lit);
                    converted.SetColor("_BaseColor", source.color);
                    converted.SetTexture("_BaseMap", source.mainTexture);
                    converted.SetTextureScale("_BaseMap", source.mainTextureScale);
                    converted.SetTextureOffset("_BaseMap", source.mainTextureOffset);
                    converted.SetFloat("_Smoothness", source.GetFloat("_Glossiness"));
                    Texture normal = source.GetTexture("_BumpMap");
                    if (normal != null)
                    {
                        converted.SetTexture("_BumpMap", normal);
                        converted.EnableKeyword("_NORMALMAP");
                    }
                    combatMaterials.Add(source, converted);
                }
                materials[i] = converted;
            }
            renderer.sharedMaterials = materials;
        }
    }

    private void SimulateZombies()
    {
        if (Phase != RailTravelPhase.Farming || zombiePrefab == null) return;
        bool firstDefense = VisitNumber == 1;
        if (firstDefense && (!FuelDefenseTriggered || !DefenseDelay.Expired(Runner))) return;
        if (EncounterVisit != VisitNumber)
            SpawnZombieEncounter();

        for (int i = 0; i < ZombieCount; i++)
        {
            ZombieState zombie = Zombies[i];
            if (zombie.Health <= 0) continue;
            zombie.Animation = 0;
            if (firstDefense && !FirstDefenseBreached && CargoDoorHealth > 0 && trainTransform != null)
            {
                AttackCargoDoor(i, ref zombie);
                Zombies.Set(i, zombie);
                continue;
            }
            PlayerMovement target = FindZombieTarget(zombie.Position);
            if (target != null)
            {
                Vector3 toward = target.transform.position - zombie.Position;
                float height = Mathf.Abs(toward.y);
                toward.y = 0f;
                float distance = toward.magnitude;
                Vector3 forward = toward.normalized;
                if (distance > 0.01f) zombie.Yaw = Quaternion.LookRotation(forward).eulerAngles.y;
                Vector3 attackPoint = target.transform.position + Vector3.up * 0.8f;
                if (distance < 1.45f && height < 1.4f &&
                    (!Physics.Linecast(zombie.Position + Vector3.up, attackPoint, out RaycastHit obstruction,
                        ~0, QueryTriggerInteraction.Ignore) || obstruction.transform.IsChildOf(target.transform)))
                {
                    zombie.Animation = 2;
                    if (zombie.AttackCooldown.ExpiredOrNotRunning(Runner))
                    {
                        target.ApplyZombieDamage(15);
                        zombie.AttackCooldown = TickTimer.CreateFromSeconds(Runner, 1.2f);
                    }
                }
                else if (distance > 0.3f && TryMoveZombie(i, ref zombie, forward))
                {
                    zombie.Animation = 1;
                }
            }
            Zombies.Set(i, zombie);
        }
    }

    private void SpawnZombieEncounter()
    {
        EncounterVisit = VisitNumber;
        ZombieCount = 0;
        int wanted = ActiveMapDanger switch { 2 => 3, 3 => 4, 4 => 6, 5 => 8, _ => 2 };
        if (VisitNumber == 1) wanted = 3;
        Physics.SyncTransforms();
        for (int attempt = 0; attempt < 64 && ZombieCount < wanted; attempt++)
        {
            float x = 7f + ((attempt * 7) % 11);
            float z = (attempt % 2 == 0 ? 1f : -1f) * (9f + attempt / 2 * 1.15f);
            Vector3 sample = VisitNumber == 1 && trainTransform != null
                ? CargoAttackPoint + new Vector3(5f + (attempt / 5) * 1.3f, 0f, (attempt % 5 - 2) * 1.8f)
                : farmingGeometry.TransformPoint(new Vector3(x, 0f, z));
            if (!TryFindZombieGround(sample, 1.5f, out Vector3 point) || IsAboard(point) ||
                Physics.CheckCapsule(point + Vector3.up * 0.38f, point + Vector3.up * 1.45f,
                    ZombieRadius, ~0, QueryTriggerInteraction.Ignore))
                continue;

            Zombies.Set(ZombieCount, new ZombieState { Position = point, Yaw = -90f, Health = ZombieHealth });
            ZombieCount++;
        }
    }

    private void AttackCargoDoor(int index, ref ZombieState zombie)
    {
        Vector3 direction = CargoAttackPoint - zombie.Position;
        direction.y = 0f;
        if (direction.sqrMagnitude > 1.45f * 1.45f)
        {
            if (TryMoveZombie(index, ref zombie, direction.normalized)) zombie.Animation = 1;
            return;
        }

        Vector3 faceDoor = CargoDoorPoint - zombie.Position;
        faceDoor.y = 0f;
        if (faceDoor.sqrMagnitude > 0.001f) zombie.Yaw = Quaternion.LookRotation(faceDoor).eulerAngles.y;
        zombie.Animation = 2;
        if (!zombie.AttackCooldown.ExpiredOrNotRunning(Runner)) return;
        DamageCargoDoor(8);
        zombie.AttackCooldown = TickTimer.CreateFromSeconds(Runner, 1.4f);
    }

    private PlayerMovement FindZombieTarget(Vector3 position)
    {
        PlayerMovement best = null;
        float nearest = 36f * 36f;
        foreach (PlayerRef player in Runner.ActivePlayers)
        {
            if (!Runner.TryGetPlayerObject(player, out NetworkObject candidate) ||
                !candidate.TryGetComponent(out PlayerMovement movement) || movement.IsDowned ||
                IsAboard(candidate.transform.position))
                continue;
            float distance = (candidate.transform.position - position).sqrMagnitude;
            if (distance >= nearest) continue;
            best = movement;
            nearest = distance;
        }
        return best;
    }

    private bool TryMoveZombie(int index, ref ZombieState zombie, Vector3 forward)
    {
        float step = 2.05f * Runner.DeltaTime;
        Vector3 away = Vector3.zero;
        for (int i = 0; i < ZombieCount; i++)
        {
            if (index == i || Zombies[i].Health <= 0) continue;
            Vector3 offset = zombie.Position - Zombies[i].Position;
            if (offset.sqrMagnitude < 0.8f * 0.8f) away += offset;
        }
        forward = (forward + away).normalized;
        if (forward.sqrMagnitude < 0.001f) return false;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            Vector3 direction = Quaternion.Euler(0f, attempt == 0 ? 0f : attempt == 1 ? 55f : -55f, 0f) * forward;
            Vector3 sample = zombie.Position + direction * step;
            if (Physics.CapsuleCast(zombie.Position + Vector3.up * 0.46f, zombie.Position + Vector3.up * 1.45f,
                    ZombieRadius, direction, step + 0.04f, ~0, QueryTriggerInteraction.Ignore) ||
                !TryFindZombieGround(sample, 0.4f, out Vector3 next) || IsAboard(next + Vector3.up))
                continue;

            zombie.Position = next;
            zombie.Yaw = Quaternion.LookRotation(direction).eulerAngles.y;
            return true;
        }
        return false;
    }

    private static bool TryFindZombieGround(Vector3 position, float height, out Vector3 point)
    {
        point = default;
        if (!Physics.Raycast(position + Vector3.up * height, Vector3.down, out RaycastHit hit,
                height * 2f, ~0, QueryTriggerInteraction.Ignore) || hit.normal.y < 0.65f)
            return false;
        point = hit.point;
        return true;
    }

    private void RenderZombies()
    {
        bool visible = Phase == RailTravelPhase.Farming && EncounterVisit == VisitNumber;
        bool newEncounter = renderedEncounter != EncounterVisit;
        for (int i = 0; i < zombieViews.Length; i++)
        {
            if (!visible || i >= ZombieCount)
            {
                if (zombieViews[i] != null) zombieViews[i].Root.SetActive(false);
                continue;
            }
            ZombieState zombie = Zombies[i];
            ZombieView view = zombieViews[i] ??= CreateZombieView();
            bool snap = newEncounter || !view.Root.activeSelf || HasStateAuthority;
            view.Root.SetActive(true);
            view.Root.transform.SetPositionAndRotation(snap ? zombie.Position :
                Vector3.Lerp(view.Root.transform.position, zombie.Position, Time.deltaTime * 18f),
                Quaternion.Euler(0f, zombie.Yaw, 0f));
            view.Head.transform.position = view.HeadBone != null ? view.HeadBone.position + Vector3.up * 0.085f
                : view.Root.transform.position + Vector3.up * 1.62f;
            view.Head.enabled = view.Body.enabled = zombie.Health > 0;
            int animation = zombie.Health <= 0 ? 3 : zombie.Animation;
            if (view.Graph.IsValid() && (newEncounter || animation != view.Animation) &&
                zombieClips[animation] != null)
            {
                if (view.Clip.IsValid()) view.Graph.DestroyPlayable(view.Clip);
                view.Clip = AnimationClipPlayable.Create(view.Graph, zombieClips[animation]);
                view.Output.SetSourcePlayable(view.Clip);
                view.Graph.Play();
            }
            view.Animation = animation;
        }
        renderedEncounter = EncounterVisit;
    }

    private ZombieView CreateZombieView()
    {
        GameObject root = Instantiate(zombiePrefab);
        ApplyCombatMaterials(root);
        Animator animator = root.GetComponentInChildren<Animator>();
        Transform headBone = System.Array.Find(root.GetComponentsInChildren<Transform>(true),
            bone => bone.name == "Base HumanHead");
        GameObject headObject = new GameObject("Zombie head hitbox");
        headObject.transform.SetParent(root.transform, false);
        SphereCollider head = headObject.AddComponent<SphereCollider>();
        head.radius = 0.16f;
        head.isTrigger = true;
        CapsuleCollider body = root.AddComponent<CapsuleCollider>();
        body.center = Vector3.up * 0.71f;
        body.radius = ZombieRadius;
        body.height = 1.43f;
        body.isTrigger = true;
        var view = new ZombieView { Root = root, HeadBone = headBone, Head = head, Body = body };
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            if (zombieClips == null)
            {
                AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
                zombieClips = System.Array.ConvertAll(ZombieAnimations,
                    name => System.Array.Find(clips, clip => clip.name == name));
            }
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            view.Graph = PlayableGraph.Create("Zombie animation");
            view.Output = AnimationPlayableOutput.Create(view.Graph, "Zombie", animator);
        }
        return view;
    }

    internal bool TryHitZombie(Collider collider, int damage, out bool weakPoint)
    {
        weakPoint = false;
        if (!Ready || !HasStateAuthority || Phase != RailTravelPhase.Farming || EncounterVisit != VisitNumber)
            return false;
        for (int i = 0; i < ZombieCount; i++)
        {
            ZombieView view = zombieViews[i];
            if (view == null || (collider != view.Head && collider != view.Body)) continue;
            ZombieState zombie = Zombies[i];
            if (zombie.Health <= 0) return false;
            weakPoint = collider == view.Head;
            zombie.Health = Mathf.Max(0, zombie.Health - damage * (weakPoint ? 2 : 1));
            Zombies.Set(i, zombie);
            return true;
        }
        return false;
    }

    private void DestroyCombatVisuals()
    {
        foreach (ZombieView view in zombieViews)
        {
            if (view == null) continue;
            if (view.Graph.IsValid()) view.Graph.Destroy();
            Destroy(view.Root);
        }
        foreach (Material material in combatMaterials.Values) Destroy(material);
    }
}
