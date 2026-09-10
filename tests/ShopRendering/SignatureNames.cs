using System.Collections.Immutable;
using System.Reflection.Metadata;

// Metadata tokens are local to each assembly. Compare the decoded type names so
// an unused stripped overload cannot fail a call to a supported overload.
internal sealed class SignatureNames : ISignatureTypeProvider<string, object?>
{
    internal static string Method(MethodSignature<string> signature)
        => $"{signature.Header.RawValue}:{signature.GenericParameterCount}:{signature.ReturnType}({string.Join(", ", signature.ParameterTypes)})";

    public string GetArrayType(string elementType, ArrayShape shape)
        => elementType + (shape.Rank == 1 ? "[*]" : "[" + new string(',', shape.Rank - 1) + "]");

    public string GetByReferenceType(string elementType) => elementType + "&";
    public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr " + Method(signature);
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
        => genericType + "<" + string.Join(", ", typeArguments) + ">";

    public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
    public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired)
        => unmodifiedType + (isRequired ? " modreq(" : " modopt(") + modifier + ")";

    public string GetPinnedType(string elementType) => "pinned " + elementType;
    public string GetPointerType(string elementType) => elementType + "*";
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => "System." + typeCode;
    public string GetSZArrayType(string elementType) => elementType + "[]";

    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        var type = reader.GetTypeDefinition(handle);
        var declaring = type.GetDeclaringType();
        return declaring.IsNil ? Qualified(reader.GetString(type.Namespace), reader.GetString(type.Name))
            : GetTypeFromDefinition(reader, declaring, rawTypeKind) + "+" + reader.GetString(type.Name);
    }

    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var type = reader.GetTypeReference(handle);
        return type.ResolutionScope.Kind != HandleKind.TypeReference
            ? Qualified(reader.GetString(type.Namespace), reader.GetString(type.Name))
            : GetTypeFromReference(reader, (TypeReferenceHandle)type.ResolutionScope, rawTypeKind) + "+" + reader.GetString(type.Name);
    }

    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    private static string Qualified(string space, string name) => space.Length == 0 ? name : space + "." + name;
}
