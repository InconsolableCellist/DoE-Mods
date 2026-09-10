using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Descent.Run
{
    /// <summary>
    /// <c>UserData/Descent/runs.json</c>: every run this machine has started or been handed by
    /// a host. Written on every change — a crash between floors must not lose the run.
    /// </summary>
    public static class RunStore
    {
        private class File_
        {
            public int Version = 1;
            public List<RunRecord> Runs = new List<RunRecord>();
        }

        private static File_ _file;

        public static IReadOnlyList<RunRecord> Runs { get { Ensure(); return _file.Runs; } }

        private static void Ensure()
        {
            if (_file != null) return;
            var path = ModPaths.RunsFile;
            try
            {
                if (File.Exists(path)) _file = JsonConvert.DeserializeObject<File_>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                Core.Log.Error($"Run file at {path} is unreadable ({e.GetType().Name}: {e.Message}); moving it aside.");
                try { File.Move(path, path + $".bad-{DateTime.Now:yyyyMMdd-HHmmss}"); } catch { }
            }
            _file ??= new File_();
            _file.Runs ??= new List<RunRecord>();
        }

        public static void Save()
        {
            Ensure();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ModPaths.RunsFile));
                var tmp = ModPaths.RunsFile + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(_file, Formatting.Indented));
                if (File.Exists(ModPaths.RunsFile)) File.Replace(tmp, ModPaths.RunsFile, null);
                else File.Move(tmp, ModPaths.RunsFile);
            }
            catch (Exception e) { Core.Log.Error($"Could not save runs: {e.GetType().Name}: {e.Message}"); }
        }

        public static RunRecord Find(string id)
        {
            Ensure();
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var r in _file.Runs) if (r.Id == id) return r;
            return null;
        }

        /// <summary>Insert or replace by id. A record handed over by a host replaces ours only if it is newer.</summary>
        public static RunRecord Upsert(RunRecord run, bool onlyIfNewer = false)
        {
            Ensure();
            for (var i = 0; i < _file.Runs.Count; i++)
            {
                if (_file.Runs[i].Id != run.Id) continue;
                if (onlyIfNewer && string.CompareOrdinal(run.UpdatedAt ?? "", _file.Runs[i].UpdatedAt ?? "") < 0) return _file.Runs[i];
                _file.Runs[i] = run;
                Save();
                return run;
            }
            _file.Runs.Insert(0, run);
            Save();
            return run;
        }

        public static void Remove(string id)
        {
            Ensure();
            _file.Runs.RemoveAll(r => r.Id == id);
            Save();
        }

        /// <summary>Unfinished runs, most recently touched first.</summary>
        public static List<RunRecord> Resumable()
        {
            Ensure();
            var list = new List<RunRecord>();
            foreach (var r in _file.Runs) if (!r.IsFinished) list.Add(r);
            list.Sort((a, b) => string.CompareOrdinal(b.UpdatedAt ?? "", a.UpdatedAt ?? ""));
            return list;
        }

        public static List<RunRecord> Recent(int max)
        {
            Ensure();
            var list = new List<RunRecord>(_file.Runs);
            list.Sort((a, b) => string.CompareOrdinal(b.UpdatedAt ?? "", a.UpdatedAt ?? ""));
            if (list.Count > max) list.RemoveRange(max, list.Count - max);
            return list;
        }
    }
}
