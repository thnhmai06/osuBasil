using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace InternalAccess;

/// <summary>One assembly opened for metadata and IL reading.</summary>
internal sealed class AssemblyModel : IDisposable
{
	private readonly Dictionary<string, TypeDefinitionHandle> _byFullName = new(StringComparer.Ordinal);

	internal AssemblyModel(string path)
	{
		Path = path;
		PE = new PEReader(File.OpenRead(path));
		Reader = PE.GetMetadataReader();
		Name = Reader.GetString(Reader.GetAssemblyDefinition().Name);
		Provider = new TypeNameProvider(this);
		foreach (var handle in Reader.TypeDefinitions)
			_byFullName[FullName(handle)] = handle;
	}

	internal string Path { get; }
	internal PEReader PE { get; }
	internal MetadataReader Reader { get; }
	internal string Name { get; }
	internal TypeNameProvider Provider { get; }

	internal bool TryGetType(string fullName, out TypeDefinitionHandle handle) =>
		_byFullName.TryGetValue(fullName, out handle);

	public void Dispose() => PE.Dispose();

	internal string FullName(TypeDefinitionHandle handle)
	{
		var type = Reader.GetTypeDefinition(handle);
		var name = Reader.GetString(type.Name);
		var declaring = type.GetDeclaringType();
		if (!declaring.IsNil) return FullName(declaring) + "+" + name;
		var ns = Reader.GetString(type.Namespace);
		return ns.Length == 0 ? name : ns + "." + name;
	}

	internal string FullName(TypeReferenceHandle handle)
	{
		var type = Reader.GetTypeReference(handle);
		var name = Reader.GetString(type.Name);
		if (type.ResolutionScope.Kind == HandleKind.TypeReference)
			return FullName((TypeReferenceHandle)type.ResolutionScope) + "+" + name;
		var ns = Reader.GetString(type.Namespace);
		return ns.Length == 0 ? name : ns + "." + name;
	}

	internal string FullName(EntityHandle handle) => handle.Kind switch
	{
		HandleKind.TypeDefinition => FullName((TypeDefinitionHandle)handle),
		HandleKind.TypeReference => FullName((TypeReferenceHandle)handle),
		HandleKind.TypeSpecification => Reader.GetTypeSpecification((TypeSpecificationHandle)handle)
			.DecodeSignature(Provider, null),
		_ => "<" + handle.Kind + ">"
	};
}

/// <summary>Renders a signature as text so a reference and a definition can be compared.</summary>
internal sealed class TypeNameProvider(AssemblyModel model) : ISignatureTypeProvider<string, object?>
{
	public string GetArrayType(string elementType, ArrayShape shape) =>
		elementType + "[" + new string(',', Math.Max(0, shape.Rank - 1)) + "]";

	public string GetByReferenceType(string elementType) => elementType + "&";
	public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";

	public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
		genericType + "<" + string.Join(",", typeArguments) + ">";

	public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
	public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;

	public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
	public string GetPinnedType(string elementType) => elementType;
	public string GetPointerType(string elementType) => elementType + "*";
	public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
	public string GetSZArrayType(string elementType) => elementType + "[]";

	public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) =>
		model.FullName(handle);

	public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) =>
		model.FullName(handle);

	public string GetTypeFromSpecification(MetadataReader reader, object? genericContext,
		TypeSpecificationHandle handle, byte rawTypeKind) =>
		reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
}

/// <summary>Reads the declared accessibility of a metadata member.</summary>
internal static class Access
{
	internal static string? OfMethod(MethodAttributes attributes) =>
		(attributes & MethodAttributes.MemberAccessMask) switch
		{
			MethodAttributes.Public => null,
			MethodAttributes.Assembly => "internal",
			MethodAttributes.FamORAssem => "protected internal",
			MethodAttributes.FamANDAssem => "private protected",
			MethodAttributes.Family => "protected",
			MethodAttributes.Private => "private",
			_ => "unknown"
		};

	internal static string? OfField(FieldAttributes attributes) =>
		(attributes & FieldAttributes.FieldAccessMask) switch
		{
			FieldAttributes.Public => null,
			FieldAttributes.Assembly => "internal",
			FieldAttributes.FamORAssem => "protected internal",
			FieldAttributes.FamANDAssem => "private protected",
			FieldAttributes.Family => "protected",
			FieldAttributes.Private => "private",
			_ => "unknown"
		};

	internal static string? OfType(TypeAttributes attributes) =>
		(attributes & TypeAttributes.VisibilityMask) switch
		{
			TypeAttributes.Public => null,
			TypeAttributes.NestedPublic => null,
			TypeAttributes.NotPublic => "internal",
			TypeAttributes.NestedAssembly => "internal",
			TypeAttributes.NestedFamORAssem => "protected internal",
			TypeAttributes.NestedFamANDAssem => "private protected",
			TypeAttributes.NestedFamily => "protected",
			TypeAttributes.NestedPrivate => "private",
			_ => "unknown"
		};

	/// <summary>True when the accessibility is visible inside the assembly but not to every caller.</summary>
	internal static bool IsAssemblyVisible(string? access) =>
		access is "internal" or "protected internal" or "private protected";
}
