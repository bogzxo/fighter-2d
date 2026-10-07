using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

using Horizon.HIDL;
using Horizon.HIDL.Runtime;

namespace Fighter2D.Content;

/// <summary>
/// Helper class for pulling values out of .hor files without every loader writing the same casts.
/// Every method complains in plain words when the file has something stupid in it, so whoever wrote the file knows what to fix.
/// </summary>
internal static class HorReader
{
    /// <summary>
    /// Runs a .hor file and returns the object it declares under the given name.
    /// </summary>
    public static Dictionary<string, IRuntimeValue> LoadObject(string file, string name)
    {
        if (!File.Exists(file)) throw new Exception($"'{file}' is missing.");

        HIDLRuntime runtime = new();
        (bool success, string result) = runtime.Evaluate(File.ReadAllText(file));
        if (!success) throw new Exception($"'{file}': {result}");

        if (runtime.UserScope.Lookup(name) is not ObjectValue found)
            throw new Exception($"'{file}' has to declare an object called '{name}'.");

        return found.Properties;
    }

    /// <summary>
    /// Helper method to get a nested object, false if it isn't there.
    /// </summary>
    public static bool TryObject(Dictionary<string, IRuntimeValue> properties, string key, out Dictionary<string, IRuntimeValue> found)
    {
        if (properties.TryGetValue(key, out var value) && value is ObjectValue nested)
        {
            found = nested.Properties;
            return true;
        }

        found = [];
        return false;
    }

    /// <summary>
    /// Helper method to get a nested object that has to be there.
    /// </summary>
    public static Dictionary<string, IRuntimeValue> Object(Dictionary<string, IRuntimeValue> properties, string key) =>
        TryObject(properties, key, out var found) ? found : throw new Exception($"{key} has to be an object.");

    /// <summary>
    /// Throws if the object has a key we have never heard of, typos in a file should not be silently ignored.
    /// </summary>
    /// <param name="renamed">Old key names and what they are called now, so the error can point at the new name.</param>
    public static void RejectUnknownKeys(Dictionary<string, IRuntimeValue> properties, HashSet<string> known, string what, Dictionary<string, string>? renamed = null)
    {
        foreach (string key in properties.Keys)
        {
            if (known.Contains(key)) continue;

            if (renamed is not null && renamed.TryGetValue(key, out string? newName))
                throw new Exception($"'{key}' is called '{newName}' these days.");

            throw new Exception($"'{key}' isn't something {what} has.");
        }
    }

    public static float Number(Dictionary<string, IRuntimeValue> properties, string key, float fallback)
    {
        if (!properties.TryGetValue(key, out var value)) return fallback;
        return value is NumberValue number ? number.Value : throw new Exception($"{key} has to be a number.");
    }

    public static bool Bool(Dictionary<string, IRuntimeValue> properties, string key, bool fallback)
    {
        if (!properties.TryGetValue(key, out var value)) return fallback;
        return value is BooleanValue boolean ? boolean.Value : throw new Exception($"{key} has to be true or false.");
    }

    public static string Text(IRuntimeValue value, string what) =>
        value is StringValue text ? text.Value : throw new Exception($"{what} has to be a text.");

    /// <summary>
    /// Helper method to read a text that has to be there.
    /// </summary>
    public static string Text(Dictionary<string, IRuntimeValue> properties, string key) =>
        properties.TryGetValue(key, out var value) ? Text(value, key) : throw new Exception($"{key} is missing.");

    public static string Text(Dictionary<string, IRuntimeValue> properties, string key, string fallback) =>
        properties.TryGetValue(key, out var value) ? Text(value, key) : fallback;

    public static Vector2 Vector(IRuntimeValue value, string what) =>
        value is Vector2Value vector ? vector.Value : throw new Exception($"{what} has to be a vec(x, y).");

    public static Vector2 Vector(Dictionary<string, IRuntimeValue> properties, string key, Vector2 fallback) =>
        properties.TryGetValue(key, out var value) ? Vector(value, key) : fallback;

    /// <summary>
    /// Helper method to read a colour written as vec(r, g, b) from 0 to 255, the renderer wants it from 0 to 1.
    /// </summary>
    public static Vector3 Colour(Dictionary<string, IRuntimeValue> properties, string key, Vector3 fallback)
    {
        if (!properties.TryGetValue(key, out var value)) return fallback;
        return value is Vector3Value colour ? colour.Value / 255f : throw new Exception($"{key} has to be a vec(r, g, b).");
    }

    /// <summary>
    /// Helper method to turn a name out of a file into an enum value.
    /// </summary>
    public static T Named<T>(string name, string what) where T : struct, Enum
    {
        string trimmed = name.Trim();

        // Enum.TryParse happily takes numbers as well, which mean fuck all to whoever reads the file
        if (trimmed.Length > 0 && !char.IsAsciiDigit(trimmed[0]) && Enum.TryParse(trimmed, ignoreCase: true, out T value) && Enum.IsDefined(value))
            return value;

        throw new Exception($"'{name}' isn't a {what}, it can be {string.Join(", ", Enum.GetNames<T>()).ToLowerInvariant()}.");
    }

    public static T Named<T>(Dictionary<string, IRuntimeValue> properties, string key, T fallback) where T : struct, Enum =>
        properties.TryGetValue(key, out var value) ? Named<T>(Text(value, key), key) : fallback;

    /// <summary>
    /// Helper method to read several texts, written as a list ["a", "b"] or as one text with the separator in it "a | b".
    /// </summary>
    public static string[] Texts(IRuntimeValue value, string what, char separator = '|')
    {
        const StringSplitOptions tidy = StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries;

        if (value is ListValue list)
        {
            var texts = new string[list.Count];
            for (int i = 0; i < texts.Length; i++) texts[i] = Text(list[i], what).Trim();
            return texts;
        }

        return value is StringValue text ? text.Value.Split(separator, tidy) : throw new Exception($"{what} has to be a text or a list of texts.");
    }

    /// <summary>
    /// Helper method to read several enum values at once, written as "standing | crouching" or ["standing", "crouching"].
    /// </summary>
    public static T Flags<T>(IRuntimeValue value, string what) where T : struct, Enum
    {
        long flags = 0;
        foreach (string name in Texts(value, what))
        {
            flags |= Convert.ToInt64(Named<T>(name, what));
        }

        return (T)Enum.ToObject(typeof(T), flags);
    }
}
