using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace StickyPro
{
    // Store records are acyclic graphs of public fields, lists and dictionaries.
    // Copy them on the UI thread; workers must never traverse live editable records.
    static class DataSnapshot
    {
        static readonly MethodInfo clone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly Dictionary<Type, FieldInfo[]> fields = new Dictionary<Type, FieldInfo[]>();
        public static object Copy(object value)
        {
            if (value == null) return null;
            Type type = value.GetType();
            if (type.IsValueType || value is string) return value;
            var dictionary = value as IDictionary;
            if (dictionary != null)
            {
                var result = (IDictionary)Activator.CreateInstance(type);
                foreach (DictionaryEntry entry in dictionary) result.Add(entry.Key, Copy(entry.Value));
                return result;
            }
            var list = value as IList;
            if (list != null)
            {
                var result = (IList)Activator.CreateInstance(type);
                foreach (var item in list) result.Add(Copy(item));
                return result;
            }
            FieldInfo[] members;
            if (!fields.TryGetValue(type, out members)) fields[type] = members = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
            object copy = clone.Invoke(value, null);
            foreach (var field in members)
                if (!field.FieldType.IsValueType && field.FieldType != typeof(string)) field.SetValue(copy, Copy(field.GetValue(value)));
            return copy;
        }
    }

    static class DataFiles
    {
        // Crash-safe save of a user file: write a temp file next to it, then swap it in. A crash or full disk
        // mid-write leaves the old file intact instead of an empty or half-written one. Falls back to a plain
        // write where the swap is not possible (some network or sync folders).
        public static void WriteAllTextAtomic(string path, string text, System.Text.Encoding encoding)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            if (!File.Exists(path)) { File.WriteAllText(path, text ?? "", encoding); return; }
            string tmp = Path.Combine(dir ?? "", "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N").Substring(0, 6) + ".tmp");
            try
            {
                File.WriteAllText(tmp, text ?? "", encoding);
                File.Replace(tmp, path, null, true);
            }
            catch (Exception ex)
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                if (ex is UnauthorizedAccessException && (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0) throw;
                File.WriteAllText(path, text ?? "", encoding);
            }
        }

        static string Full(string path) { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar; }
        public static void CheckSeparate(string from, string to)
        {
            string source = Full(from), target = Full(to);
            if (source.StartsWith(target, StringComparison.OrdinalIgnoreCase) || target.StartsWith(source, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Source and destination must be separate folders, outside each other.");
            CheckParents(from); CheckParents(to);
        }
        static void CheckParents(string path)
        {
            for (var dir = new DirectoryInfo(Path.GetFullPath(path)); dir != null; dir = dir.Parent)
                if (dir.Exists && (dir.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Choose a regular folder, without junctions or symbolic links: " + dir.FullName);
        }
        public static void CopyTree(string from, string to)
        {
            CheckSeparate(from, to);
            CopyDirectory(new DirectoryInfo(from), to, true);
        }
        static void CopyDirectory(DirectoryInfo source, string target, bool root)
        {
            if ((source.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Cannot copy a linked folder: " + source.FullName);
            Directory.CreateDirectory(target);
            foreach (var file in source.EnumerateFiles())
            {
                if ((file.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Cannot copy a linked file: " + file.FullName);
                if (file.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                file.CopyTo(Path.Combine(target, file.Name), false);
            }
            foreach (var child in source.EnumerateDirectories())
            {
                if (root && (child.Name.Equals("restore-pending", StringComparison.OrdinalIgnoreCase) || child.Name.StartsWith("before-restore", StringComparison.OrdinalIgnoreCase))) continue;
                CopyDirectory(child, Path.Combine(target, child.Name), false);
            }
        }
    }
}
