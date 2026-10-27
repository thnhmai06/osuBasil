using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text;

namespace InternalAccess;

internal enum MemberKind
{
	Method,
	Constructor,
	Field,
	Property,
	Event,
	Type
}

internal sealed record Hit(
	string DeclaringAssembly,
	string DeclaringModule,
	string DeclaringType,
	string Member,
	MemberKind Kind,
	string Access,
	string UsingAssembly,
	string UsingModule,
	string UsingSite,
	string Note);

internal sealed record UseContext(
	AssemblyModel Source,
	string Module,
	string UsingType,
	string Site,
	string AssemblyLabel);

/// <summary>Finds every use of an assembly-visible member from a different module.</summary>
internal sealed class Scanner
{
	private static readonly char[] Separators =
		['<', '>', ',', '[', ']', '*', '&', '(', ')', ':', ' '];

	private readonly AssemblyModel[] _targets;
	private readonly List<Hit> _hits = [];

	internal Scanner(params AssemblyModel[] targets) => _targets = targets;

	internal IReadOnlyList<Hit> Hits => _hits;
	internal List<string> Ambiguities { get; } = [];
	internal List<string> Failures { get; } = [];
	internal int MethodsScanned { get; private set; }

	/// <summary>Scans every method body, field, signature and local of one assembly.</summary>
	/// <param name="source">The assembly whose IL is read.</param>
	/// <param name="moduleOf">Maps a type full name of <paramref name="source" /> to its module.</param>
	/// <param name="assemblyLabel">How the assembly is named in the report.</param>
	internal void Scan(AssemblyModel source, Func<string, string, string> moduleOf)
	{
		var reader = source.Reader;
		var assemblyLabel = source.Name;
		foreach (var typeHandle in reader.TypeDefinitions)
		{
			var type = reader.GetTypeDefinition(typeHandle);
			var typeFullName = source.FullName(typeHandle);
			var module = moduleOf(source.Name, typeFullName);
			var usingType = Outer(typeFullName);

			if (!type.BaseType.IsNil)
				RecordTypeName(source.FullName(type.BaseType),
					new UseContext(source, module, usingType, usingType + " (base type)", assemblyLabel));

			foreach (var implementationHandle in type.GetInterfaceImplementations())
			{
				var implementation = reader.GetInterfaceImplementation(implementationHandle);
				RecordTypeName(source.FullName(implementation.Interface),
					new UseContext(source, module, usingType, usingType + " (interface)", assemblyLabel));
			}

			foreach (var fieldHandle in type.GetFields())
			{
				var field = reader.GetFieldDefinition(fieldHandle);
				var fieldName = reader.GetString(field.Name);
				var label = fieldName.StartsWith("<>9__", StringComparison.Ordinal)
					? usingType + " (cached delegate)"
					: usingType + "." + fieldName;
				var context = new UseContext(source, module, usingType, label, assemblyLabel);
				RecordSignatureTypes(field.DecodeSignature(source.Provider, null), context);
			}

			foreach (var methodHandle in type.GetMethods())
			{
				var method = reader.GetMethodDefinition(methodHandle);
				var site = SiteLabel(source, typeHandle, methodHandle);
				var context = new UseContext(source, module, usingType, site, assemblyLabel);

				var signature = method.DecodeSignature(source.Provider, null);
				RecordSignatureTypes(signature.ReturnType, context);
				foreach (var parameter in signature.ParameterTypes)
					RecordSignatureTypes(parameter, context);

				if (method.RelativeVirtualAddress == 0) continue;

				MethodsScanned++;
				MethodBodyBlock body;
				try
				{
					body = source.PE.GetMethodBody(method.RelativeVirtualAddress);
				}
				catch (Exception exception)
				{
					Failures.Add($"{site}: {exception.Message}");
					continue;
				}

				if (!body.LocalSignature.IsNil)
				foreach (var local in reader.GetStandaloneSignature(body.LocalSignature)
					         .DecodeLocalSignature(source.Provider, null))
					RecordSignatureTypes(local, context);

				foreach (var instruction in IlReader.Read(body.GetILBytes() ?? []))
				{
					if (instruction.Token is not { } token) continue;
					EntityHandle handle;
					try
					{
						handle = MetadataTokens.EntityHandle(token);
					}
					catch
					{
						continue;
					}

					HandleToken(handle, context);
				}
			}
		}
	}

	private void HandleToken(EntityHandle handle, UseContext context)
	{
		switch (handle.Kind)
		{
			case HandleKind.MemberReference:
				HandleMemberReference((MemberReferenceHandle)handle, context);
				break;
			case HandleKind.MethodDefinition:
				HandleMethod((MethodDefinitionHandle)handle, context);
				break;
			case HandleKind.FieldDefinition:
				HandleField((FieldDefinitionHandle)handle, context);
				break;
			case HandleKind.MethodSpecification:
			{
				var specification = context.Source.Reader.GetMethodSpecification((MethodSpecificationHandle)handle);
				foreach (var argument in specification.DecodeSignature(context.Source.Provider, null))
					RecordSignatureTypes(argument, context);
				HandleToken(specification.Method, context);
				break;
			}
			case HandleKind.TypeDefinition:
			case HandleKind.TypeReference:
			case HandleKind.TypeSpecification:
				RecordSignatureTypes(context.Source.FullName(handle), context);
				break;
		}
	}

	private void HandleMemberReference(MemberReferenceHandle handle, UseContext context)
	{
		var reader = context.Source.Reader;
		var reference = reader.GetMemberReference(handle);
		var name = reader.GetString(reference.Name);
		var parentText = context.Source.FullName(reference.Parent);
		var parentName = OpenName(parentText);

		RecordSignatureTypes(parentText, context, parentName);

		var target = _targets.FirstOrDefault(model => model.TryGetType(parentName, out _));
		if (target is null) return;

		if (reference.GetKind() == MemberReferenceKind.Method)
		{
			var signature = reference.DecodeMethodSignature(context.Source.Provider, null);
			ResolveMethod(target, parentName, name, MethodKey(name, signature), context);
		}
		else
		{
			var fieldType = reference.DecodeFieldSignature(context.Source.Provider, null);
			ResolveField(target, parentName, name, fieldType, context);
		}
	}

	private void HandleMethod(MethodDefinitionHandle handle, UseContext context)
	{
		var typeHandle = context.Source.Reader.GetMethodDefinition(handle).GetDeclaringType();
		ResolveMethod(context.Source, context.Source.FullName(typeHandle), handle, context);
	}

	private void HandleField(FieldDefinitionHandle handle, UseContext context)
	{
		var typeHandle = context.Source.Reader.GetFieldDefinition(handle).GetDeclaringType();
		var typeFullName = context.Source.FullName(typeHandle);
		RecordMember(context.Source, typeFullName, handle, context);
	}

	private void ResolveMethod(AssemblyModel target, string typeFullName, string name, string key,
		UseContext context)
	{
		if (!target.TryGetType(typeFullName, out var typeHandle))
		{
			Ambiguities.Add($"{typeFullName}.{name}: declaring type not found");
			return;
		}

		var reader = target.Reader;
		var candidates = new List<MethodDefinitionHandle>();
		MethodDefinitionHandle? match = null;
		foreach (var methodHandle in reader.GetTypeDefinition(typeHandle).GetMethods())
		{
			var method = reader.GetMethodDefinition(methodHandle);
			if (reader.GetString(method.Name) != name) continue;
			candidates.Add(methodHandle);
			if (MethodKey(name, method.DecodeSignature(target.Provider, null)) == key)
			{
				match = methodHandle;
				break;
			}
		}

		if (match is null)
		{
			if (candidates.Count == 1) match = candidates[0];
			else
			{
				Ambiguities.Add(candidates.Count == 0
					? $"{typeFullName}.{name}: no definition for '{key}'"
					: $"{typeFullName}.{name}: {candidates.Count} overloads, '{key}' matched none");
				return;
			}
		}

		RecordMember(target, typeFullName, match.Value, context);
	}

	private void ResolveMethod(AssemblyModel target, string typeFullName, MethodDefinitionHandle handle,
		UseContext context)
	{
		if (target.TryGetType(typeFullName, out var typeHandle) &&
		    target.Reader.GetMethodDefinition(handle).GetDeclaringType() == typeHandle)
			RecordMember(target, typeFullName, handle, context);
	}

	private void ResolveField(AssemblyModel target, string typeFullName, string name, string fieldType,
		UseContext context)
	{
		if (!target.TryGetType(typeFullName, out var typeHandle))
		{
			Ambiguities.Add($"{typeFullName}.{name}: declaring type not found");
			return;
		}

		var reader = target.Reader;
		var candidates = new List<FieldDefinitionHandle>();
		FieldDefinitionHandle? match = null;
		foreach (var fieldHandle in reader.GetTypeDefinition(typeHandle).GetFields())
		{
			var field = reader.GetFieldDefinition(fieldHandle);
			if (reader.GetString(field.Name) != name) continue;
			candidates.Add(fieldHandle);
			if (field.DecodeSignature(target.Provider, null) == fieldType)
			{
				match = fieldHandle;
				break;
			}
		}

		if (match is null)
		{
			if (candidates.Count == 1) match = candidates[0];
			else
			{
				Ambiguities.Add($"{typeFullName}.{name}: {candidates.Count} fields, '{fieldType}' matched none");
				return;
			}
		}

		RecordMember(target, typeFullName, match.Value, context);
	}

	private void RecordMember(AssemblyModel target, string typeFullName, MethodDefinitionHandle handle,
		UseContext context)
	{
		var reader = target.Reader;
		var method = reader.GetMethodDefinition(handle);
		var name = reader.GetString(method.Name);
		var typeHandle = method.GetDeclaringType();
		var typeAccess = Access.OfType(reader.GetTypeDefinition(typeHandle).Attributes);
		var memberAccess = Access.OfMethod(method.Attributes);
		var kind = name is ".ctor" or ".cctor" ? MemberKind.Constructor : MemberKind.Method;

		var property = PropertyName(target, typeHandle, handle);
		if (property is not null)
		{
			name = property;
			kind = MemberKind.Property;
		}

		Record(target, typeFullName, name, kind, typeAccess, memberAccess, context);
	}

	private void RecordMember(AssemblyModel target, string typeFullName, FieldDefinitionHandle handle,
		UseContext context)
	{
		var reader = target.Reader;
		var field = reader.GetFieldDefinition(handle);
		var name = reader.GetString(field.Name);
		var typeAccess = Access.OfType(reader.GetTypeDefinition(field.GetDeclaringType()).Attributes);
		var memberAccess = Access.OfField(field.Attributes);
		Record(target, typeFullName, name, MemberKind.Field, typeAccess, memberAccess, context);
	}

	private void Record(AssemblyModel target, string typeFullName, string name, MemberKind kind,
		string? typeAccess, string? memberAccess, UseContext context)
	{
		if (!Access.IsAssemblyVisible(typeAccess) && !Access.IsAssemblyVisible(memberAccess)) return;

		var declaringModule = ModuleOf(target.Name, typeFullName);
		if (declaringModule == context.Module) return;

		var access = string.Join(" + ",
			new[] { typeAccess, memberAccess }.Where(Access.IsAssemblyVisible).Distinct());

		_hits.Add(new Hit(target.Name, declaringModule, Display(typeFullName), name, kind, access,
			context.AssemblyLabel, context.Module, context.Site, "member use"));
	}

	private void RecordTypeName(string fullName, UseContext context)
	{
		foreach (var target in _targets)
		{
			if (!target.TryGetType(fullName, out var handle)) continue;
			var definition = target.Reader.GetTypeDefinition(handle);
			var access = Access.OfType(definition.Attributes);
			if (!Access.IsAssemblyVisible(access)) return;

			var declaringModule = ModuleOf(target.Name, fullName);
			if (declaringModule == context.Module) return;

			_hits.Add(new Hit(target.Name, declaringModule, Display(fullName), Display(fullName),
				MemberKind.Type, access!, context.AssemblyLabel, context.Module, context.Site, "type use"));
			return;
		}
	}

	private void RecordSignatureTypes(string text, UseContext context, string? skip = null)
	{
		foreach (var token in text.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
		{
			if (token == skip) continue;
			RecordTypeName(token, context);
		}
	}

	private static string? PropertyName(AssemblyModel target, TypeDefinitionHandle typeHandle,
		MethodDefinitionHandle handle)
	{
		var reader = target.Reader;
		foreach (var propertyHandle in reader.GetTypeDefinition(typeHandle).GetProperties())
		{
			var property = reader.GetPropertyDefinition(propertyHandle);
			var accessors = property.GetAccessors();
			if (accessors.Getter == handle || accessors.Setter == handle ||
			    accessors.Others.Contains(handle))
				return reader.GetString(property.Name);
		}

		foreach (var eventHandle in reader.GetTypeDefinition(typeHandle).GetEvents())
		{
			var @event = reader.GetEventDefinition(eventHandle);
			var accessors = @event.GetAccessors();
			if (accessors.Adder == handle || accessors.Remover == handle || accessors.Raiser == handle ||
			    accessors.Others.Contains(handle))
				return reader.GetString(@event.Name);
		}

		return null;
	}

	private static string MethodKey(string name, MethodSignature<string> signature) =>
		$"{name}`{signature.GenericParameterCount}({string.Join(",", signature.ParameterTypes)}):{signature.ReturnType}";

	internal static string OpenName(string text)
	{
		var index = text.IndexOf('<');
		if (index < 0) index = text.IndexOf('[');
		return index < 0 ? text : text[..index];
	}

	internal static string Display(string fullName) => fullName.Replace('+', '.');

	internal static string Outer(string fullName) => fullName.Split('+')[0];

	/// <summary>Names the method that contains the instruction, resolving the name of a compiler-generated body.</summary>
	private static string SiteLabel(AssemblyModel source, TypeDefinitionHandle typeHandle,
		MethodDefinitionHandle methodHandle)
	{
		var reader = source.Reader;
		var typeFullName = source.FullName(typeHandle);
		var methodName = reader.GetString(reader.GetMethodDefinition(methodHandle).Name);
		var type = Outer(typeFullName);
		var nested = typeFullName.Length > type.Length ? typeFullName[(type.Length + 1)..] : "";

		if (methodName == "MoveNext" && nested.StartsWith('<'))
		{
			var end = nested.IndexOf('>');
			if (end > 1) methodName = nested[1..end];
		}
		else if (methodName.StartsWith('<'))
		{
			var end = methodName.IndexOf('>');
			if (end > 1) methodName = methodName[1..end];
		}

		return $"{Display(type)}.{methodName}";
	}

	/// <summary>Names the module a type belongs to, from the first namespace segment under its project.</summary>
	internal static string ModuleOf(string assemblyName, string typeFullName)
	{
		if (assemblyName is not ("Basil.Application.Runtime" or "Basil.Application.Capabilities"))
			return assemblyName;

		var outer = Outer(typeFullName);
		if (!outer.StartsWith(assemblyName + ".", StringComparison.Ordinal))
			return "Generated";

		var rest = outer[(assemblyName.Length + 1)..];
		if (!rest.Contains('.'))
			return assemblyName.EndsWith("Capabilities", StringComparison.Ordinal)
				? "Root(Capabilities)"
				: "Root(Runtime)";

		var module = rest.Split('.')[0];
		return module == "Connections" ? "Users" : module;
	}
}
