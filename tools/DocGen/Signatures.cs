using System.Globalization;
using System.Reflection;
using System.Text;

namespace DocGen;

/// <summary>C# declarations of types and members, and the identifiers used by XML documentation files.</summary>
internal static class Signatures
{
    private static readonly Dictionary<Type, string> Keywords = new()
    {
        [typeof(void)] = "void", [typeof(bool)] = "bool", [typeof(byte)] = "byte", [typeof(sbyte)] = "sbyte", [typeof(short)] = "short",
        [typeof(ushort)] = "ushort", [typeof(int)] = "int", [typeof(uint)] = "uint", [typeof(long)] = "long", [typeof(ulong)] = "ulong",
        [typeof(float)] = "float", [typeof(double)] = "double", [typeof(decimal)] = "decimal", [typeof(char)] = "char",
        [typeof(string)] = "string", [typeof(object)] = "object",
    };

    private static readonly NullabilityInfoContext Nullability = new();

    // ---- Type names -------------------------------------------------------------------------------------------------

    public static string TypeName(Type t, NullabilityInfo? info = null)
    {
        if (t.IsByRef) return TypeName(t.GetElementType()!, info?.ElementType);
        if (t.IsArray)
            return TypeName(t.GetElementType()!, info?.ElementType) + "[" + new string(',', t.GetArrayRank() - 1) + "]" + QuestionMark(t, info);
        if (t.IsGenericParameter) return t.Name + (info?.ReadState == NullabilityState.Nullable ? "?" : "");
        if (Nullable.GetUnderlyingType(t) is { } underlying) return TypeName(underlying) + "?";
        if (Keywords.TryGetValue(t, out string? keyword)) return keyword + QuestionMark(t, info);

        string name = t.Name;
        int tick = name.IndexOf('`');
        if (tick >= 0) name = name[..tick];
        var arguments = t.IsGenericType ? t.GetGenericArguments() : [];
        if (t.IsNested && t.DeclaringType is { } declaring)
        {
            int inherited = declaring.IsGenericType ? declaring.GetGenericArguments().Length : 0;
            arguments = arguments.Skip(inherited).ToArray();
            name = TypeName(declaring) + "." + name;
        }
        if (t.IsGenericType && t.FullName?.StartsWith("System.ValueTuple`", StringComparison.Ordinal) == true)
            return "(" + string.Join(", ", arguments.Select((a, i) => TypeName(a, info?.GenericTypeArguments.ElementAtOrDefault(i)))) + ")" + QuestionMark(t, info);
        if (arguments.Length > 0)
            name += "<" + string.Join(", ", arguments.Select((a, i) => TypeName(a, info?.GenericTypeArguments.ElementAtOrDefault(i)))) + ">";
        return name + QuestionMark(t, info);
    }

    private static string QuestionMark(Type t, NullabilityInfo? info) => !t.IsValueType && info?.ReadState == NullabilityState.Nullable ? "?" : "";

    // ---- Declarations -----------------------------------------------------------------------------------------------

    public static string Kind(Type t) =>
        t.IsEnum ? "enum"
        : t.IsInterface ? "interface"
        : t.IsValueType ? "struct"
        : t.GetMethod("<Clone>$") is not null ? "record"
        : "class";

    public static string TypeDeclaration(Type t)
    {
        var sb = new StringBuilder("public ");
        if (t.IsClass && t.IsAbstract && t.IsSealed) sb.Append("static ");
        else if (t.IsClass && t.IsAbstract) sb.Append("abstract ");
        else if (t.IsClass && t.IsSealed) sb.Append("sealed ");
        else if (t.IsValueType && !t.IsEnum && t.CustomAttributes.Any(a => a.AttributeType.Name == "IsReadOnlyAttribute")) sb.Append("readonly ");
        sb.Append(Kind(t)).Append(' ').Append(TypeName(t));
        var bases = new List<string>();
        if (t.IsEnum) bases.Add(TypeName(Enum.GetUnderlyingType(t)));
        else
        {
            if (t.BaseType is { } b && b != typeof(object) && b != typeof(ValueType) && b != typeof(Enum)) bases.Add(TypeName(b));
            var inheritedFromBase = t.BaseType?.GetInterfaces() ?? [];
            foreach (var i in t.GetInterfaces().Where(i => !inheritedFromBase.Contains(i)).OrderBy(i => i.Name, StringComparer.Ordinal))
            {
                if (t.GetInterfaces().Any(other => other != i && other.GetInterfaces().Contains(i))) continue; // implied by a more specific interface
                bases.Add(TypeName(i));
            }
        }
        if (bases.Count > 0) sb.Append(" : ").Append(string.Join(", ", bases));
        sb.Append(GenericConstraints(t.IsGenericType ? t.GetGenericArguments() : []));
        return sb.ToString();
    }

    private static string GenericConstraints(Type[] parameters)
    {
        var sb = new StringBuilder();
        foreach (var p in parameters)
        {
            var parts = new List<string>();
            var attributes = p.GenericParameterAttributes;
            bool unmanaged = p.CustomAttributes.Any(a => a.AttributeType.Name == "IsUnmanagedAttribute");
            if (attributes.HasFlag(GenericParameterAttributes.ReferenceTypeConstraint)) parts.Add("class");
            if (unmanaged) parts.Add("unmanaged");
            else if (attributes.HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint)) parts.Add("struct");
            foreach (var c in p.GetGenericParameterConstraints())
                if (c != typeof(ValueType)) parts.Add(TypeName(c));
            if (attributes.HasFlag(GenericParameterAttributes.DefaultConstructorConstraint) && !attributes.HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint)) parts.Add("new()");
            if (parts.Count > 0) sb.Append(" where ").Append(p.Name).Append(" : ").Append(string.Join(", ", parts));
        }
        return sb.ToString();
    }

    public static string Parameter(ParameterInfo p, bool extensionThis)
    {
        var sb = new StringBuilder();
        if (extensionThis) sb.Append("this ");
        Type t = p.ParameterType;
        if (p.IsOut) sb.Append("out ");
        else if (t.IsByRef) sb.Append(p.IsIn ? "in " : "ref ");
        else if (p.GetCustomAttribute<ParamArrayAttribute>() is not null) sb.Append("params ");
        sb.Append(TypeName(t, Nullability.Create(p))).Append(' ').Append(p.Name);
        if (p.HasDefaultValue) sb.Append(" = ").Append(Literal(p.RawDefaultValue, t));
        return sb.ToString();
    }

    private static string Literal(object? value, Type type)
    {
        if (value is null || value == DBNull.Value) return type.IsValueType && Nullable.GetUnderlyingType(type) is null ? "default" : "null";
        Type real = Nullable.GetUnderlyingType(type) ?? type;
        if (real.IsEnum) return TypeName(real) + "." + Enum.ToObject(real, value);
        return value switch
        {
            string s => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"",
            bool b => b ? "true" : "false",
            char c => "'" + c + "'",
            long l => l.ToString(CultureInfo.InvariantCulture) + "L",
            float f => f.ToString("R", CultureInfo.InvariantCulture) + "f",
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "null",
        };
    }

    private static string Modifiers(MethodBase m)
    {
        var sb = new StringBuilder("public ");
        if (m.IsStatic) sb.Append("static ");
        else if (m is MethodInfo mi)
        {
            if (mi.IsAbstract) sb.Append("abstract ");
            else if (mi.GetBaseDefinition() != mi && mi.IsVirtual) sb.Append(mi.IsFinal ? "sealed override " : "override ");
            else if (mi.IsVirtual && !mi.IsFinal) sb.Append("virtual ");
        }
        return sb.ToString();
    }

    public static string Method(MethodInfo m)
    {
        var sb = new StringBuilder(Modifiers(m));
        var ps = m.GetParameters();
        bool extension = m.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false);
        if (m.Name.StartsWith("op_", StringComparison.Ordinal) && m.IsSpecialName)
        {
            string op = OperatorSymbol(m.Name);
            if (m.Name is "op_Implicit" or "op_Explicit")
                sb.Append(m.Name == "op_Implicit" ? "implicit" : "explicit").Append(" operator ").Append(TypeName(m.ReturnType));
            else
                sb.Append(TypeName(m.ReturnType, Nullability.Create(m.ReturnParameter))).Append(" operator ").Append(op);
        }
        else
        {
            sb.Append(ReturnTypeName(m)).Append(' ').Append(m.Name);
            if (m.IsGenericMethod) sb.Append('<').Append(string.Join(", ", m.GetGenericArguments().Select(a => a.Name))).Append('>');
        }
        sb.Append('(').Append(string.Join(", ", ps.Select((p, i) => Parameter(p, extension && i == 0)))).Append(')');
        if (m.IsGenericMethod) sb.Append(GenericConstraints(m.GetGenericArguments()));
        return sb.ToString();
    }

    /// <summary>The return type; a tuple shows the element names given in the source.</summary>
    private static string ReturnTypeName(MethodInfo m)
    {
        string plain = TypeName(m.ReturnType, Nullability.Create(m.ReturnParameter));
        var names = m.ReturnParameter.GetCustomAttribute<System.Runtime.CompilerServices.TupleElementNamesAttribute>()?.TransformNames;
        if (names is null || !plain.StartsWith('(')) return plain;
        var types = m.ReturnType.GetGenericArguments();
        return "(" + string.Join(", ", types.Select((t, i) => TypeName(t) + " " + names[i])) + ")";
    }

    private static string OperatorSymbol(string name) => name switch
    {
        "op_Equality" => "==", "op_Inequality" => "!=", "op_LessThan" => "<", "op_GreaterThan" => ">", "op_LessThanOrEqual" => "<=",
        "op_GreaterThanOrEqual" => ">=", "op_Addition" => "+", "op_Subtraction" => "-", "op_Multiply" => "*", "op_Division" => "/",
        "op_UnaryNegation" => "-", "op_UnaryPlus" => "+", "op_LogicalNot" => "!", "op_True" => "true", "op_False" => "false",
        _ => name[3..],
    };

    public static string Constructor(ConstructorInfo c)
    {
        var ps = c.GetParameters();
        return $"public {c.DeclaringType!.Name.Split('`')[0]}({string.Join(", ", ps.Select(p => Parameter(p, false)))})";
    }

    public static string Property(PropertyInfo p)
    {
        var sb = new StringBuilder("public ");
        var getter = p.GetMethod?.IsPublic == true ? p.GetMethod : null;
        var setter = p.SetMethod?.IsPublic == true ? p.SetMethod : null;
        var anyAccessor = getter ?? setter!;
        if (anyAccessor.IsStatic) sb.Append("static ");
        else if (anyAccessor.IsAbstract) sb.Append("abstract ");
        else if (anyAccessor.GetBaseDefinition() != anyAccessor && anyAccessor.IsVirtual) sb.Append("override ");
        else if (anyAccessor.IsVirtual && !anyAccessor.IsFinal) sb.Append("virtual ");
        sb.Append(TypeName(p.PropertyType, Nullability.Create(p))).Append(' ');
        var index = p.GetIndexParameters();
        if (index.Length > 0) sb.Append("this[").Append(string.Join(", ", index.Select(i => Parameter(i, false)))).Append(']');
        else sb.Append(p.Name);
        sb.Append(" { ");
        if (getter is not null) sb.Append("get; ");
        if (setter is not null)
        {
            bool init = setter.ReturnParameter.GetRequiredCustomModifiers().Any(m => m.Name == "IsExternalInit");
            sb.Append(init ? "init; " : "set; ");
        }
        sb.Append('}');
        return sb.ToString();
    }

    public static string Field(FieldInfo f)
    {
        var sb = new StringBuilder("public ");
        if (f.IsLiteral) sb.Append("const ");
        else
        {
            if (f.IsStatic) sb.Append("static ");
            if (f.IsInitOnly) sb.Append("readonly ");
        }
        sb.Append(TypeName(f.FieldType, Nullability.Create(f))).Append(' ').Append(f.Name);
        if (f.IsLiteral) sb.Append(" = ").Append(Literal(f.GetRawConstantValue(), f.FieldType));
        return sb.ToString();
    }

    // ---- XML documentation identifiers ------------------------------------------------------------------------------

    public static string IdOfType(Type t) => "T:" + TypeId(t);

    public static string Id(MemberInfo m)
    {
        string owner = TypeId(m.DeclaringType!);
        return m switch
        {
            MethodInfo mi => "M:" + owner + "." + mi.Name + (mi.IsGenericMethod ? "``" + mi.GetGenericArguments().Length : "") + Params(mi.GetParameters())
                + (mi.Name is "op_Implicit" or "op_Explicit" ? "~" + ParamId(mi.ReturnType) : ""),
            ConstructorInfo ci => "M:" + owner + ".#ctor" + Params(ci.GetParameters()),
            PropertyInfo pi => "P:" + owner + "." + pi.Name + Params(pi.GetIndexParameters()),
            FieldInfo fi => "F:" + owner + "." + fi.Name,
            EventInfo ei => "E:" + owner + "." + ei.Name,
            Type nested => "T:" + TypeId(nested),
            _ => throw new NotSupportedException(m.GetType().Name),
        };
    }

    private static string TypeId(Type t) => (t.Namespace is { Length: > 0 } ns ? ns + "." : "") + NestedName(t);

    private static string NestedName(Type t)
    {
        if (t.IsNested && t.DeclaringType is { } d) return NestedName(d) + "." + t.Name;
        return t.Name;
    }

    private static string Params(ParameterInfo[] ps) => ps.Length == 0 ? "" : "(" + string.Join(",", ps.Select(p => ParamId(p.ParameterType))) + ")";

    private static string ParamId(Type t)
    {
        if (t.IsByRef) return ParamId(t.GetElementType()!) + "@";
        if (t.IsArray)
        {
            int rank = t.GetArrayRank();
            return ParamId(t.GetElementType()!) + (rank == 1 ? "[]" : "[" + string.Join(",", Enumerable.Repeat("0:", rank)) + "]");
        }
        if (t.IsGenericParameter) return (t.DeclaringMethod is not null ? "``" : "`") + t.GenericParameterPosition;
        if (t.IsGenericType)
        {
            string definition = TypeId(t.GetGenericTypeDefinition());
            definition = definition[..definition.IndexOf('`')];
            return definition + "{" + string.Join(",", t.GetGenericArguments().Select(ParamId)) + "}";
        }
        return TypeId(t);
    }

    /// <summary>A stable anchor for an identifier: lower case letters and digits separated by single dashes.</summary>
    public static string Anchor(string id)
    {
        var sb = new StringBuilder();
        bool dash = false;
        foreach (char c in id[2..])
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                if (dash && sb.Length > 0) sb.Append('-');
                sb.Append(char.ToLowerInvariant(c));
                dash = false;
            }
            else dash = true;
        }
        return sb.ToString();
    }
}
