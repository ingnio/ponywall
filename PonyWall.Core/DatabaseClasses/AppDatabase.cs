using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace pylorak.TinyWall.DatabaseClasses
{
    [DataContract(Namespace = "TinyWall")]
    class AppDatabase : ISerializable<AppDatabase>
    {
        internal static Func<string, List<FirewallExceptionV3>, int>? GuiPromptCallback;

        [DataMember(Name = "KnownApplications")]
        private readonly List<Application> _KnownApplications;

        public static string DBPath
        {
            get { return System.IO.Path.Combine(Utils.AppDataPath, "profiles.json"); }
        }

        private const string EmbeddedSeedResourceName = "pylorak.TinyWall.DatabaseClasses.profiles.json";

        public static AppDatabase Load()
        {
            // Try the on-disk copy first. This is where the service persists
            // any modifications (new known apps, user edits flushed through
            // the pipe handler, etc.). Empty/missing/corrupt is handled by
            // the fallback below.
            try
            {
                if (System.IO.File.Exists(DBPath) && new System.IO.FileInfo(DBPath).Length > 0)
                    return SerializationHelper.DeserializeFromFile(DBPath, new AppDatabase(), readOnlySource: true);
            }
            catch
            {
                // Fall through to embedded fallback. We do NOT re-throw or log
                // here because the embedded fallback produces a working app,
                // and the caller's try/catch (App.OnFrameworkInitializationCompleted)
                // would otherwise catch this and leave us with an empty
                // AppDatabase — exactly the bug this method is fixing.
            }

            // Fall back to the embedded seed shipped in PonyWall.Core.dll. This
            // is the source of truth for a fresh install where no disk file
            // exists yet (dev builds, manual extract, bin\Release launches
            // that bypass the installer, first run after a PonyWall→PonyWall
            // upgrade that wipes ProgramData, ...). The embedded copy is
            // linked from installer/profiles.json at build time — see the
            // EmbeddedResource entry in PonyWall.Core.csproj.
            var asm = typeof(AppDatabase).Assembly;
            using var seedStream = asm.GetManifestResourceStream(EmbeddedSeedResourceName)
                ?? throw new System.IO.FileNotFoundException(
                    $"Embedded seed resource '{EmbeddedSeedResourceName}' is missing from {asm.GetName().Name}. " +
                    $"Check the EmbeddedResource entry in PonyWall.Core.csproj.");
            return SerializationHelper.Deserialize(seedStream, new AppDatabase());
        }

        public void Save(string filePath)
        {
            SerializationHelper.SerializeToFile(this, filePath);
        }

        [JsonConstructor]
        public AppDatabase(List<Application> knownApplications)
        {
            _KnownApplications = knownApplications;
        }

        public AppDatabase() :
            this(new List<Application>())
        { }

        public List<Application> KnownApplications
        {
            get { return _KnownApplications; }
        }

        public Application? GetApplicationByName(string name)
        {
            foreach (Application app in _KnownApplications)
            {
                if (app.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase))
                    return app;
            }

            return null;
        }

        public List<FirewallExceptionV3> FastSearchMachineForKnownApps()
        {
            var ret = new List<FirewallExceptionV3>();

            foreach (DatabaseClasses.Application app in KnownApplications)
            {
                if (app.HasFlag("TWUI:Special"))
                    continue;

                foreach (SubjectIdentity id in app.Components)
                {
                    List<ExceptionSubject> subjects = id.SearchForFile();
                    foreach (var subject in subjects)
                    {
                        ret.Add(id.InstantiateException(subject));
                    }
                }
            }

            return ret;
        }

        internal Application? TryGetApp(ExecutableSubject fromSubject, out FirewallExceptionV3? fwex, bool matchSpecial)
        {
            foreach (var app in KnownApplications)
            {
                if (!matchSpecial && app.HasFlag("TWUI:Special"))
                    continue;

                foreach (var id in app.Components)
                {
                    if (id.DoesExecutableSatisfy(fromSubject))
                    {
                        fwex = id.InstantiateException(fromSubject);
                        return app;
                    }
                }
            }

            fwex = null;
            return null;
        }

        internal List<FirewallExceptionV3> GetExceptionsForApp(ExceptionSubject fromSubject, bool guiPrompt, out Application? app)
        {
            app = null;
            var exceptions = new List<FirewallExceptionV3>();

            if (fromSubject is AppContainerSubject)
            {
                exceptions.Add(new FirewallExceptionV3(fromSubject, new TcpUdpPolicy(true)));
                return exceptions;
            }
            else if (fromSubject is ExecutableSubject exeSubject)
            {
                // Try to find an application this subject might belong to
                app = TryGetApp(exeSubject, out FirewallExceptionV3? _, false);
                if (app == null)
                {
                    exceptions.Add(new FirewallExceptionV3(exeSubject, new TcpUdpPolicy(true)));
                    return exceptions;
                }

                // Now that we have the app, try to instantiate firewall exceptions
                // for all components.
                string pathHint = System.IO.Path.GetDirectoryName(exeSubject.ExecutablePath);
                foreach (SubjectIdentity id in app.Components)
                {
                    List<ExceptionSubject> foundSubjects = id.SearchForFile(pathHint);
                    foreach (ExceptionSubject subject in foundSubjects)
                    {
                        var tmp = id.InstantiateException(subject);
                        if (fromSubject.Equals(subject))
                            // Make sure original subject is at index 0
                            exceptions.Insert(0, tmp);
                        else
                            exceptions.Add(tmp);
                    }
                }

                // If we have found dependencies, ask the user what to do
                if ((exceptions.Count > 1) && guiPrompt)
                {
                    // Try to get localized name
                    string localizedAppName = Resources.Exceptions.ResourceManager.GetString(app.Name);
                    localizedAppName = string.IsNullOrEmpty(localizedAppName) ? app.Name : localizedAppName;

                    int result = 101; // Default: unblock all
                    if (GuiPromptCallback != null)
                        result = GuiPromptCallback(localizedAppName, exceptions);

                    switch (result)
                    {
                        case 101:
                            break;
                        case 102:
                            // Remove all exceptions with a different subject than the input argument
                            for (int i = exceptions.Count - 1; i >= 0; --i)
                            {
                                if (exceptions[i].Subject is ExecutableSubject exesub)
                                {
                                    if (!exesub.ExecutablePath.Equals(exeSubject.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                                    {
                                        exceptions.RemoveAt(i);
                                        continue;
                                    }
                                }
                                else
                                {
                                    exceptions.RemoveAt(i);
                                    continue;
                                }
                            }
                            exceptions.RemoveRange(1, exceptions.Count - 1);
                            break;
                        case 103:
                            exceptions.Clear();
                            break;
                    }
                }
            }
            else
            {
                throw new NotImplementedException();
            }

            return exceptions;
        }

        public JsonTypeInfo<AppDatabase> GetJsonTypeInfo()
        {
            return SourceGenerationContext.Default.AppDatabase;
        }
    }
}
