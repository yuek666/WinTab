using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace WindowTabManager
{
    public sealed class WindowInfo
    {
        public IntPtr Handle;
        public uint ProcessId;
        public string Key, Executable, ClassName, Title, Monitor, AppName;
        public bool Minimized;
        public long LastUsed;
        public string Family { get { return (Executable ?? "").ToUpperInvariant() + "|" + ClassName; } }
    }

    [DataContract]
    public sealed class WindowRule
    {
        [DataMember] public string Executable;
        [DataMember] public string ClassName;
        [DataMember] public string Title;
        [DataMember] public string GroupId;
        [DataMember] public bool HideAlt;
        [DataMember] public bool HideWin;
        public string Family { get { return (Executable ?? "").ToUpperInvariant() + "|" + ClassName; } }
        public static WindowRule From(WindowInfo w, string group)
        {
            return new WindowRule { Executable = w.Executable, ClassName = w.ClassName, Title = w.Title, GroupId = group };
        }
    }

    [DataContract]
    public sealed class GroupInfo
    {
        [DataMember] public string Id;
        [DataMember] public string Name;
        public override string ToString() { return Name; }
    }

    [DataContract]
    public sealed class Profile
    {
        [DataMember] public string Name = "未命名";
        [DataMember] public List<GroupInfo> Groups = new List<GroupInfo>();
        [DataMember] public List<WindowRule> Rules = new List<WindowRule>();
    }

    [DataContract]
    public sealed class Settings
    {
        [DataMember] public int Version = 1;
        [DataMember] public int StartupSlot = -1;
        [DataMember] public bool UseActiveMonitor;
        [DataMember] public Profile[] Slots = new Profile[3];
        [DataMember] public List<WindowRule> Visibility = new List<WindowRule>();
    }

    public static class JsonStore
    {
        public static T Clone<T>(T value)
        {
            using (var stream = new MemoryStream()) {
                var serializer = new DataContractJsonSerializer(typeof(T));
                serializer.WriteObject(stream, value); stream.Position = 0;
                return (T)serializer.ReadObject(stream);
            }
        }
        public static Settings Load(string path)
        {
            if (!File.Exists(path)) return new Settings();
            Settings settings;
            using (var stream = File.OpenRead(path)) settings = (Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(stream);
            if (settings == null || settings.Version != 1 || settings.Slots == null || settings.Slots.Length != 3 ||
                settings.Visibility == null || settings.StartupSlot < -1 || settings.StartupSlot > 2)
                throw new SerializationException("不支援或損壞的設定格式。");
            foreach (Profile profile in settings.Slots.Where(p => p != null)) {
                if (profile.Groups == null || profile.Rules == null || profile.Groups.Any(g => g == null || String.IsNullOrEmpty(g.Id) || String.IsNullOrWhiteSpace(g.Name)) ||
                    profile.Groups.Select(g => g.Id).Distinct().Count() != profile.Groups.Count)
                    throw new SerializationException("群組資料損壞。");
                ValidateRules(profile.Rules);
            }
            ValidateRules(settings.Visibility);
            return settings;
        }
        private static void ValidateRules(List<WindowRule> rules)
        {
            if (rules.Any(r => r == null || r.Executable == null || r.ClassName == null || r.Title == null))
                throw new SerializationException("視窗規則損壞。");
        }
        public static void Save(string path, Settings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temp = path + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None)) {
                new DataContractJsonSerializer(typeof(Settings)).WriteObject(stream, settings);
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
            else File.Move(temp, path);
        }
    }

    public static class Matcher
    {
        // No handles on disk. Never guess between duplicate titles or multiple windows.
        public static Dictionary<string, WindowRule> Match(IEnumerable<WindowInfo> windows, IEnumerable<WindowRule> rules)
        {
            var result = new Dictionary<string, WindowRule>();
            var live = windows.Where(w => !String.IsNullOrEmpty(w.Executable)).ToArray();
            var saved = rules.Where(r => !String.IsNullOrEmpty(r.Executable)).ToArray();
            foreach (var family in live.GroupBy(w => w.Family)) {
                var candidates = family.ToArray();
                var entries = saved.Where(r => r.Family == family.Key).ToArray();
                foreach (var w in candidates) {
                    var exact = entries.Where(r => String.Equals(r.Title, w.Title, StringComparison.Ordinal)).ToArray();
                    if (exact.Length == 1 && candidates.Count(c => c.Title == w.Title) == 1) result[w.Key] = exact[0];
                    else if (exact.Length == 0 && candidates.Length == 1 && entries.Length == 1) result[w.Key] = entries[0];
                }
            }
            return result;
        }
    }

    public sealed class Workspace
    {
        public Profile Current = new Profile { Name = "本次工作區" };
        public bool Dirty;
        public int LoadedSlot = -1;
        public WindowInfo[] Windows = new WindowInfo[0];
        private readonly Dictionary<string, string> assignments = new Dictionary<string, string>();
        private readonly Dictionary<string, WindowRule> matchedRules = new Dictionary<string, WindowRule>();
        private readonly Dictionary<string, WindowRule> visibility = new Dictionary<string, WindowRule>();
        private readonly HashSet<string> seen = new HashSet<string>();
        private readonly HashSet<string> seenVisibility = new HashSet<string>();
        public Settings Settings;
        public Workspace(Settings settings) { Settings = settings; }

        public void Update(WindowInfo[] windows)
        {
            Windows = windows;
            var mapped = Matcher.Match(windows, Current.Rules);
            var hidden = Matcher.Match(windows, Settings.Visibility);
            foreach (var w in windows) {
                WindowRule rule;
                if (seen.Add(w.Key)) {
                    assignments[w.Key] = mapped.TryGetValue(w.Key, out rule) && Current.Groups.Any(g => g.Id == rule.GroupId) ? rule.GroupId : null;
                    if (rule != null) matchedRules[w.Key] = rule;
                }
                if (seenVisibility.Add(w.Key) && hidden.TryGetValue(w.Key, out rule)) visibility[w.Key] = rule;
            }
            // Keep live assignments across title changes, but discard all dead HWND identities.
            var keys = new HashSet<string>(windows.Select(w => w.Key));
            foreach (string key in seen.Where(k => !keys.Contains(k)).ToArray()) { seen.Remove(key); assignments.Remove(key); matchedRules.Remove(key); }
            foreach (string key in seenVisibility.Where(k => !keys.Contains(k)).ToArray()) { seenVisibility.Remove(key); visibility.Remove(key); }
        }
        public string GroupOf(WindowInfo w) { string group; return assignments.TryGetValue(w.Key, out group) ? group : null; }
        public string GroupName(WindowInfo w) { var group = Current.Groups.FirstOrDefault(g => g.Id == GroupOf(w)); return group == null ? "未分組" : group.Name; }
        public bool Hidden(WindowInfo w, bool alt) { WindowRule r; return visibility.TryGetValue(w.Key, out r) && (alt ? r.HideAlt : r.HideWin); }
        public void Move(WindowInfo w, string group)
        {
            assignments[w.Key] = group; Dirty = true;
            if (!String.IsNullOrEmpty(w.Executable)) {
                WindowRule rule;
                if (!matchedRules.TryGetValue(w.Key, out rule)) { rule = WindowRule.From(w, group); Current.Rules.Add(rule); matchedRules[w.Key] = rule; }
                rule.GroupId = group; rule.Title = w.Title;
            }
        }
        public void SetVisibility(WindowInfo w, bool alt, bool hidden)
        {
            WindowRule r;
            if (!visibility.TryGetValue(w.Key, out r)) {
                r = WindowRule.From(w, null);
                var attached = new HashSet<WindowRule>(visibility.Values);
                Settings.Visibility.RemoveAll(old => old.Family == r.Family && old.Title == r.Title && !attached.Contains(old));
                visibility[w.Key] = r; Settings.Visibility.Add(r);
            }
            r.Title = w.Title;
            if (alt) r.HideAlt = hidden; else r.HideWin = hidden;
        }
        public GroupInfo AddGroup(string name)
        {
            var group = new GroupInfo { Id = Guid.NewGuid().ToString("N"), Name = name.Trim() };
            Current.Groups.Add(group); Dirty = true; return group;
        }
        public void DeleteGroup(GroupInfo group)
        {
            Current.Groups.RemoveAll(g => g.Id == group.Id);
            foreach (string key in assignments.Keys.ToArray()) if (assignments[key] == group.Id) assignments[key] = null;
            foreach (var r in Current.Rules) if (r.GroupId == group.Id) r.GroupId = null;
            Dirty = true;
        }
        public void AutoGroup()
        {
            Current = new Profile { Name = "依應用程式分組" };
            assignments.Clear(); matchedRules.Clear(); seen.Clear();
            foreach (var family in Windows.GroupBy(w => String.IsNullOrEmpty(w.Executable) ? "restricted:" + w.ProcessId : w.Executable, StringComparer.OrdinalIgnoreCase)) {
                var group = AddGroup(family.First().AppName);
                foreach (var w in family) {
                    assignments[w.Key] = group.Id; seen.Add(w.Key);
                    if (!String.IsNullOrEmpty(w.Executable)) {
                        var rule = WindowRule.From(w, group.Id); Current.Rules.Add(rule); matchedRules[w.Key] = rule;
                    }
                }
            }
            LoadedSlot = -1; Dirty = true;
        }
        public void Load(int slot)
        {
            Current = Settings.Slots[slot] == null ? new Profile { Name = "組態 " + (slot + 1) } : JsonStore.Clone(Settings.Slots[slot]);
            assignments.Clear(); matchedRules.Clear(); seen.Clear(); LoadedSlot = slot; Update(Windows); Dirty = false;
        }
        public Profile Capture(string name)
        {
            var p = new Profile { Name = name, Groups = JsonStore.Clone(Current.Groups) };
            // Preserve rules for absent windows, replace rules associated with live identities.
            var replaced = new HashSet<WindowRule>(matchedRules.Values);
            p.Rules.AddRange(Current.Rules.Where(r => !replaced.Contains(r) && !Windows.Any(w => r.Family == w.Family && r.Title == w.Title)).Select(JsonStore.Clone));
            foreach (var w in Windows.Where(w => !String.IsNullOrEmpty(w.Executable))) {
                // Replace all live title identities as a batch, preserving duplicate ambiguity without accumulating rules.
                p.Rules.Add(WindowRule.From(w, GroupOf(w)));
            }
            return p;
        }
        public void AcceptSaved(int slot, Profile profile)
        {
            Settings.Slots[slot] = profile;
            Current = JsonStore.Clone(profile); LoadedSlot = slot; Dirty = false;
            matchedRules.Clear();
            foreach (var pair in Matcher.Match(Windows, Current.Rules)) matchedRules[pair.Key] = pair.Value;
        }
    }
}
