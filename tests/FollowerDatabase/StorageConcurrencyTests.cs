using System.Collections;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using pitTeam.Server.Services;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Json;

internal static class StorageConcurrencyTests
{
    public static void Run(string work, Action<bool, string> check)
    {
        string initialDirectory = Environment.CurrentDirectory;
        string sandbox = Path.Combine(work, "service-concurrency");
        Directory.CreateDirectory(sandbox);
        Environment.CurrentDirectory = sandbox;
        try
        {
            var services = new ServiceCollection();
            services.AddSingleton(new FileUtil());
            services.AddSingleton(new JsonUtil([new SptJsonConverterRegistrator()]));
            services.AddSingleton(DispatchProxy.Create<ISptLogger<FriendlyTeammateStorage>, TestLogger>());
            // Use SPT's actual registration path, including the production Injectable lifetime.
            var injection = new DependencyInjectionHandler(services);
            injection.AddInjectableTypesFromTypeList([typeof(FriendlyTeammateStorage)]);
            injection.InjectAll();
            using var provider = services.BuildServiceProvider(validateScopes: true);
            using var writeScope = provider.CreateScope();
            using var readScope = provider.CreateScope();
            var writer = writeScope.ServiceProvider.GetRequiredService<FriendlyTeammateStorage>();
            var reader = readScope.ServiceProvider.GetRequiredService<FriendlyTeammateStorage>();
            var session = new MongoId("eeeeeeeeeeeeeeeeeeeeeeee");
            writer.InitializeProfile(session);
            reader.InitializeProfile(session);

            using var writeOpened = new ManualResetEventSlim();
            using var releaseWrite = new ManualResetEventSlim();
            using var readStarted = new ManualResetEventSlim();
            Task write = Task.Run(() => writer.WriteBatch(session, new PausedDocuments(
                new Dictionary<string, string> { ["100.json"] = "{\"aid\":100}" }, writeOpened, releaseWrite)));
            Task<List<SPTarkov.Server.Core.Models.Eft.Common.Tables.BotBase>>? read = null;
            bool readerWaited;
            try
            {
                if (!writeOpened.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Writer did not open its transaction.");
                read = Task.Run(() =>
                {
                    readStarted.Set();
                    return reader.ReadProfiles(session);
                });
                if (!readStarted.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Reader did not start.");
                // Keep the transaction open across a second request's roster read.
                readerWaited = Task.WhenAny(read, Task.Delay(250)).GetAwaiter().GetResult() != read;
            }
            finally
            {
                releaseWrite.Set();
                write.GetAwaiter().GetResult();
            }
            var profiles = read!.GetAwaiter().GetResult();
            check(readerWaited, "overlapping request waits for the active database transaction");
            check(profiles.Count == 1 && profiles[0].Aid == 100, "overlapping roster read sees the committed teammate");
            check(ReferenceEquals(writer, reader), "SPT resolves the same storage owner across request scopes");

            Parallel.For(0, 12, new ParallelOptions { MaxDegreeOfParallelism = 8 }, index =>
            {
                using var scope = provider.CreateScope();
                var storage = scope.ServiceProvider.GetRequiredService<FriendlyTeammateStorage>();
                int aid = 200 + index;
                storage.InitializeProfile(session);
                storage.WriteBatch(session, new Dictionary<string, string>
                {
                    [$"{aid}.json"] = $"{{\"aid\":{aid}}}",
                    [$"{aid}-settings.json"] = "{\"AutoJoinEnabled\":true}",
                    [$"{aid}-equipment.json"] = "[]",
                });
                if (!storage.Exists(session, $"{aid}-settings.json")
                    || storage.ReadProfiles(session).All(profile => profile.Aid != aid))
                    throw new Exception("Concurrent request lost its saved teammate.");
            });
            check(reader.ReadProfiles(session).Count == 13, "concurrent request scopes preserve every created teammate");
            Parallel.For(0, 12, index =>
            {
                using var scope = provider.CreateScope();
                var storage = scope.ServiceProvider.GetRequiredService<FriendlyTeammateStorage>();
                int aid = 200 + index;
                if (!storage.DeleteTeammate(session, aid)
                    || storage.Exists(session, $"{aid}-settings.json")
                    || storage.Exists(session, $"{aid}-equipment.json"))
                    throw new Exception("Concurrent teammate deletion left active documents.");
            });
            check(reader.ReadProfiles(session).Count == 1, "concurrent deletions leave unrelated teammates intact");
        }
        finally { Environment.CurrentDirectory = initialDirectory; }
    }

    private sealed class PausedDocuments(
        IReadOnlyDictionary<string, string> values, ManualResetEventSlim opened, ManualResetEventSlim release)
        : IReadOnlyDictionary<string, string>
    {
        public string this[string key] => values[key];
        public IEnumerable<string> Keys => values.Keys;
        public IEnumerable<string> Values => values.Values;
        public int Count => values.Count;
        public bool ContainsKey(string key) => values.ContainsKey(key);
        public bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
        {
            opened.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Paused transaction was not released.");
            return values.GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
