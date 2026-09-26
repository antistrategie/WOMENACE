using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Il2CppInterop.Runtime.InteropTypes
{
    public class Il2CppObjectBase
    {
        private static long _next;
        public IntPtr Pointer { get; set; } = (IntPtr)(++_next);
        public T TryCast<T>() where T : class => this as T;
    }
}

namespace Il2CppMenace.Tactical
{
    public class Entity : Il2CppObjectBase { }
    public class Actor : Entity
    {
        public DamageInfo OnDamageReceived(Entity attacker, Skill skill, DamageInfo damage) => damage;
    }
    public class DamageInfo
    {
        public int Damage = 80;
        public int ArmorDamage = 30;
        public bool IsAoE = true;
        // Sentinel for a damage side effect which the waiver must not change.
        public int Suppression = 17;
    }
}

namespace Il2CppMenace.Tactical.Skills
{
    [Flags]
    public enum SkillType { None = 0, StatusEffect = 1, DamageEffect = 2 }
    public class Tag { public string name; }
    public class SkillTemplate : Il2CppObjectBase
    {
        public string Id;
        public bool IsAttack = true;
        public SkillType Type;
        public List<Tag> Tags = new();
        public string GetID() => Id;
    }
    public class Skill : Il2CppObjectBase
    {
        public SkillTemplate Template;
        public Actor Source;
        public Skill SourceSkill;
        public SkillTemplate GetTemplate() => Template;
    }
}

namespace Il2CppMenace.Tactical.Skills.Effects
{
    public class DeathrattleTemplate { public SkillTemplate Skill; }
    public class DeathrattleHandler : Il2CppObjectBase
    {
        public Skill ParentSkill;
        public DeathrattleTemplate m_Template;
        public Actor Actor;
        public Actor GetActor() => Actor;
    }
}

namespace Jiangyu.Sdk
{
    public class PatchInfo
    {
        public object Instance;
        public List<object> Args = new();
    }
    public class Patches
    {
        private readonly Dictionary<(string, string, int, bool), Action<PatchInfo>> _hooks = new();
        public void Prefix(string type, string method, Action<PatchInfo> hook) => Prefix(type, method, -1, hook);
        public void Prefix(string type, string method, int arity, Action<PatchInfo> hook) =>
            _hooks.Add((type, method, arity, false), hook);
        public void Postfix(string type, string method, int arity, Action<PatchInfo> hook) =>
            _hooks.Add((type, method, arity, true), hook);
        public void Invoke(object instance, string method, int arity, bool postfix, params object[] args)
        {
            var key = (instance.GetType().FullName, method, arity, postfix);
            if (_hooks.TryGetValue(key, out var hook))
                hook(new PatchInfo { Instance = instance, Args = args.ToList() });
            else if (!postfix)
                throw new Exception($"Missing prefix: {key}");
        }
    }
    public class Log
    {
        public void Debug(string message) { }
        public void Warn(string message) => throw new Exception(message);
    }
    public class SystemContext
    {
        public Patches Patches = new();
        public Log Log = new();
    }
    public abstract class JiangyuSystem
    {
        public SystemContext Context = new();
        public virtual void OnInit() { }
        public virtual void OnSceneLoaded(int buildIndex, string sceneName) { }
        public virtual void OnUnload() { }
    }
}

namespace HarmonyLib
{
    public static class AccessTools
    {
        public static MethodInfo DeclaredMethod(Type type, string name, Type[] parameters) =>
            type.GetMethod(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, parameters);
    }

    public sealed class HarmonyMethod
    {
        public readonly MethodInfo Method;
        public HarmonyMethod(Type type, string name) =>
            Method = type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new MissingMethodException(type.FullName, name);
    }

    // Models Harmony's callback order, not native detouring. A postfix is skipped
    // when the original throws. A finalizer still runs, with per-call prefix state.
    public sealed class Harmony
    {
        public delegate void DamagePrefix(Actor victim, Entity attacker, Skill skill, DamageInfo damage, out object state);
        public delegate Exception DamageFinalizer(Exception error, object state);
        private static (string Owner, DamagePrefix Prefix, DamageFinalizer Finalizer)? _damage;
        private readonly string _id;

        public static bool HasDamagePatch => _damage != null;
        public Harmony(string id) => _id = id;

        public void Patch(MethodInfo original, HarmonyMethod prefix = null, HarmonyMethod finalizer = null)
        {
            if (original.DeclaringType != typeof(Actor) || original.Name != nameof(Actor.OnDamageReceived))
                throw new InvalidOperationException("Unexpected Harmony target");
            if (_damage != null)
                throw new InvalidOperationException("Damage patch was not unloaded");
            _damage = (_id, prefix.Method.CreateDelegate<DamagePrefix>(), finalizer.Method.CreateDelegate<DamageFinalizer>());
        }

        public void UnpatchSelf()
        {
            if (_damage?.Owner == _id)
                _damage = null;
        }

        public static void Receive(Actor victim, Entity attacker, Skill skill, DamageInfo damage, Action body,
            Action beforePrefix = null, Action afterPrefix = null, Action postfix = null, bool skipOriginal = false)
        {
            var patch = _damage ?? throw new InvalidOperationException("Damage patch is absent");
            object state = null;
            Exception error = null;
            try
            {
                beforePrefix?.Invoke();
                patch.Prefix(victim, attacker, skill, damage, out state);
                afterPrefix?.Invoke();
                if (!skipOriginal)
                    body?.Invoke();
                postfix?.Invoke();
            }
            catch (Exception ex)
            {
                error = ex;
            }
            error = patch.Finalizer(error, state);
            if (error != null)
                ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}
