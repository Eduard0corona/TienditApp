using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace FoodParkDigital.Application.Tests.Support;

/// <summary>
/// Builds a domain entity with every stored member set to a distinct value (private setters, backing fields and
/// child collections included), so a mapping test notices a member that is not copied, copied from the wrong source
/// or sorted wrongly. Values are deterministic but unsorted.
/// </summary>
internal sealed class EntityFiller
{
    private const string DomainNamespace = "FoodParkDigital.Domain";
    private readonly int _maxDepth;
    private int _seed;

    public EntityFiller(int maxDepth = 2, int seed = 1)
    {
        _maxDepth = maxDepth;
        _seed = seed;
    }

    public T Create<T>() where T : class => (T)Create(typeof(T), depth: 0);

    /// <summary>Sets one member after filling (a navigation to null, a status the test needs).</summary>
    public static void Set(object target, string member, object? value)
    {
        var property = FindProperty(target.GetType(), member);
        if (property?.GetSetMethod(nonPublic: true) is { } setter)
        {
            setter.Invoke(target, [value]);
            return;
        }

        var field = FindField(target.GetType(), $"<{member}>k__BackingField")
            ?? FindField(target.GetType(), "_" + char.ToLowerInvariant(member[0]) + member[1..])
            ?? throw new InvalidOperationException($"{target.GetType().Name}.{member} cannot be set.");
        field.SetValue(target, value);
    }

    private object Create(Type type, int depth)
    {
        var instance = RuntimeHelpers.GetUninitializedObject(type);
        InitializeCollectionFields(instance, type);

        foreach (var property in AllProperties(type))
        {
            if (property.GetIndexParameters().Length > 0)
                continue;

            var propertyType = property.PropertyType;
            if (IsCollectionOfEntities(propertyType, out var elementType))
            {
                if (depth >= _maxDepth)
                    continue;
                var list = CollectionStore(instance, type, property);
                if (list is null)
                    continue;
                for (var i = 0; i < 2; i++)
                    list.Add(Create(elementType, depth + 1));
                continue;
            }

            if (!CanStore(instance, type, property))
                continue;

            object? value;
            if (IsDomainClass(propertyType))
            {
                if (depth >= _maxDepth)
                    continue;
                value = Create(propertyType, depth + 1);
            }
            else if (!TryCreateValue(propertyType, out value))
            {
                continue;
            }

            Store(instance, type, property, value);
        }

        return instance;
    }

    private bool TryCreateValue(Type type, out object? value)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        var n = Next();
        value = underlying switch
        {
            _ when underlying == typeof(string) => $"v{n}",
            _ when underlying == typeof(Guid) => new Guid(n, 0, 0, new byte[8]),
            _ when underlying == typeof(bool) => true,
            _ when underlying == typeof(int) => n,
            _ when underlying == typeof(long) => (long)n,
            _ when underlying == typeof(short) => (short)(n % short.MaxValue),
            _ when underlying == typeof(byte) => (byte)(n % 255),
            _ when underlying == typeof(decimal) => n / 100m,
            _ when underlying == typeof(double) => n / 100d,
            _ when underlying == typeof(DateTimeOffset) => new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(n),
            _ when underlying == typeof(DateTime) => new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(n),
            _ when underlying == typeof(DateOnly) => new DateOnly(2026, 1, 1).AddDays(n % 300),
            _ when underlying == typeof(TimeOnly) => new TimeOnly(n % 24, n % 60),
            _ when underlying == typeof(TimeSpan) => TimeSpan.FromMinutes(n % 1440),
            _ when underlying == typeof(byte[]) => new byte[] { 1, 2, (byte)(n % 255) },
            _ when underlying.IsEnum => PickEnum(underlying, n),
            _ => null
        };
        return value is not null;
    }

    /// <summary>Unsorted but repeatable: 37, 74, 11, 48, ... (mod 97), never 0.</summary>
    private int Next() => (_seed++ * 37 % 97) + 1;

    private static object PickEnum(Type enumType, int n)
    {
        var values = Enum.GetValues(enumType);
        // A non-default value, so a member left at its default is noticed.
        var candidates = values.Cast<object>().Where(v => Convert.ToInt64(v) != 0).ToList();
        return candidates.Count == 0 ? values.GetValue(0)! : candidates[n % candidates.Count];
    }

    private static bool IsDomainClass(Type type)
        => type.IsClass && !type.IsAbstract && type != typeof(string)
            && type.Namespace?.StartsWith(DomainNamespace, StringComparison.Ordinal) == true;

    private static bool IsCollectionOfEntities(Type type, out Type elementType)
    {
        elementType = typeof(object);
        if (!type.IsGenericType || type == typeof(string))
            return false;
        var definition = type.GetGenericTypeDefinition();
        if (definition != typeof(IReadOnlyCollection<>) && definition != typeof(ICollection<>)
            && definition != typeof(List<>) && definition != typeof(IEnumerable<>) && definition != typeof(IReadOnlyList<>))
            return false;
        elementType = type.GetGenericArguments()[0];
        return IsDomainClass(elementType);
    }

    private static IList? CollectionStore(object instance, Type type, PropertyInfo property)
    {
        var field = FindField(type, "_" + char.ToLowerInvariant(property.Name[0]) + property.Name[1..]);
        if (field?.GetValue(instance) is IList fieldList)
            return fieldList;

        if (property.GetSetMethod(nonPublic: true) is { } setter)
        {
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(property.PropertyType.GetGenericArguments()[0]))!;
            setter.Invoke(instance, [list]);
            return list;
        }

        var backing = FindField(type, $"<{property.Name}>k__BackingField");
        if (backing is not null)
        {
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(property.PropertyType.GetGenericArguments()[0]))!;
            backing.SetValue(instance, list);
            return list;
        }

        return null;
    }

    /// <summary>Uninitialized objects skip field initializers: create the private lists the collections read.</summary>
    private static void InitializeCollectionFields(object instance, Type type)
    {
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                if (field.GetValue(instance) is not null)
                    continue;
                var fieldType = field.FieldType;
                if (fieldType.IsGenericType && (fieldType.GetGenericTypeDefinition() == typeof(List<>)
                    || fieldType.GetGenericTypeDefinition() == typeof(HashSet<>)))
                {
                    field.SetValue(instance, Activator.CreateInstance(fieldType));
                }
                else if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(ICollection<>))
                {
                    field.SetValue(instance, Activator.CreateInstance(typeof(List<>).MakeGenericType(fieldType.GetGenericArguments()[0])));
                }
            }
        }
    }

    private static bool CanStore(object instance, Type type, PropertyInfo property)
        => property.GetSetMethod(nonPublic: true) is not null || FindField(type, $"<{property.Name}>k__BackingField") is not null;

    private static void Store(object instance, Type type, PropertyInfo property, object? value)
    {
        if (property.GetSetMethod(nonPublic: true) is { } setter)
            setter.Invoke(instance, [value]);
        else
            FindField(type, $"<{property.Name}>k__BackingField")!.SetValue(instance, value);
    }

    private static IEnumerable<PropertyInfo> AllProperties(Type type)
    {
        var seen = new HashSet<string>();
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            foreach (var property in current.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (seen.Add(property.Name))
                    yield return property;
            }
        }
    }

    private static PropertyInfo? FindProperty(Type type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var property = current.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (property is not null)
                return property;
        }

        return null;
    }

    private static FieldInfo? FindField(Type type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var field = current.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
            if (field is not null)
                return field;
        }

        return null;
    }
}
