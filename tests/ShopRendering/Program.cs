using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: ShopRendering.Tests <mod.dll> <game UnityEngine.UIElementsModule.dll>");
    return 2;
}

using var modStream = File.OpenRead(args[0]);
using var gameStream = File.OpenRead(args[1]);
using var modPe = new PEReader(modStream);
using var gamePe = new PEReader(gameStream);
var mod = modPe.GetMetadataReader();
var game = gamePe.GetMetadataReader();
var signatures = new SignatureNames();
var calls = new HashSet<(string Type, string Method, string Signature)>();
foreach (var handle in mod.MemberReferences)
{
    var member = mod.GetMemberReference(handle);
    if (member.GetKind() != MemberReferenceKind.Method || member.Parent.Kind != HandleKind.TypeReference)
        continue;
    var type = mod.GetTypeReference((TypeReferenceHandle)member.Parent);
    var name = mod.GetString(type.Name);
    if (mod.GetString(type.Namespace) == "UnityEngine.UIElements")
        calls.Add((name, mod.GetString(member.Name), SignatureNames.Method(member.DecodeMethodSignature(signatures, (object?)null))));
}

if (calls.Count == 0)
{
    Console.Error.WriteLine("FAIL The supplied mod has no checked UI API references");
    return 1;
}

var failed = 0;
foreach (var call in calls.OrderBy(call => call.Type).ThenBy(call => call.Method).ThenBy(call => call.Signature))
{
    var typeHandle = game.TypeDefinitions.FirstOrDefault(handle =>
    {
        var candidate = game.GetTypeDefinition(handle);
        return game.GetString(candidate.Namespace) == "UnityEngine.UIElements" && game.GetString(candidate.Name) == call.Type;
    });
    if (typeHandle.IsNil)
    {
        Console.Error.WriteLine($"FAIL {call.Type}.{call.Method}: type not present in the installed UI assembly");
        failed++;
        continue;
    }
    var type = game.GetTypeDefinition(typeHandle);
    var methods = type.GetMethods().Select(game.GetMethodDefinition)
        .Where(method => game.GetString(method.Name) == call.Method
            && SignatureNames.Method(method.DecodeSignature(signatures, (object?)null)) == call.Signature).ToArray();
    // Wrappers either call native bindings directly or contain restored managed code.
    // A stripped placeholder also has a method body, but throws before doing any work.
    var native = type.GetFields().Select(game.GetFieldDefinition).Any(field =>
        game.GetString(field.Name).StartsWith("NativeMethodInfoPtr_" + call.Method.Replace('.', '_') + "_", StringComparison.Ordinal));
    var throwing = methods.Any(method => IsStrippedPlaceholder(method));
    var restored = methods.All(method => method.RelativeVirtualAddress != 0);
    if (methods.Length == 0 || (!native && !restored) || throwing)
    {
        Console.Error.WriteLine($"FAIL {call.Type}.{call.Method}: no supported UI implementation for {call.Signature}");
        failed++;
    }
    else
        Console.WriteLine($"PASS {call.Type}.{call.Method}: {(native ? "native binding present" : "restored managed implementation")}");
}

Console.WriteLine($"Checked {calls.Count} UI API references against the installed game assemblies");
return failed == 0 ? 0 : 1;

bool IsStrippedPlaceholder(MethodDefinition method)
{
    if (method.RelativeVirtualAddress == 0)
        return false;
    var body = gamePe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes();
    // Il2CppInterop's unsupported method body loads the error string, constructs
    // NotSupportedException and throws. Read metadata without loading Unity.
    if (body is not { Length: >= 11 } || body[0] != 0x72 || body[5] != 0x73 || body[10] != 0x7a)
        return false;
    var token = BitConverter.ToInt32(body, 1);
    return (token & unchecked((int)0xff000000)) == 0x70000000
        && game.GetUserString(MetadataTokens.UserStringHandle(token & 0x00ffffff)) == "Method unstripping failed";
}
