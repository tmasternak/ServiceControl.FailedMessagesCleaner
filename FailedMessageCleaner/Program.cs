using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NLog.Config;
using NLog.Targets;
using Raven.Client.Embedded;
using ServiceControl.MessageFailures;
using ServiceControl.MessageFailures.Api;
using ServiceControl.Recoverability;

namespace FailedMessageCleaner
{
    class Program
    {
        static async Task Main(string[] args)
        {
            ConfigureLogging();

            if (args.Length == 0)
            {
                Console.WriteLine("Usage: FileMessageCleaner.exe <db_path>");
                return;
            }

            var path = args[0]; // @"C:\ProgramData\Particular\ServiceControl\Particular.Rabbitmq\DB";

            log.Info($"Connecting to RavenDB instance at {path} ...");

            var store = RavenBootstrapper.Setup(path, 33334);
            store.Initialize();

            log.Info($"Connected. Processing FailedMessage documents ...");

            await CleanFailedMessages(store).ConfigureAwait(false);
            log.Info($"Clean-up finished press <any key> to exit ...");
            Console.ReadLine();

            store.Dispose();
        }

        static async Task CleanFailedMessages(EmbeddableDocumentStore store)
        {
            int start = 0;
            //This makes sure we don't do more than 30 operations per session
            int pageSize = 15;
            int maxAttemptsPerMessage = 10;

            var ids = new List<string>();

            while (true)
            {
                ids.Clear();

                using (var session = store.OpenAsyncSession())
                {
                    var page = await session.Advanced
                        .AsyncDocumentQuery<FailureGroupMessageView, FailedMessages_ByGroup>()
                        .AddOrder("MessageId", true)
                        .Take(pageSize)
                        .Skip(start)
                        .SetResultTransformer(new FailedMessageViewTransformer().TransformerName)
                        .SelectFields<FailedMessageView>()
                        .ToListAsync()
                        .ConfigureAwait(false);

                    start += page.Count;

                    if (page.Count == 0)
                    {
                        log.Info($"Cleaned up {0} documents.", start);
                        return;
                    }

                    var idsToPrune = page
                        .Where(i => i.NumberOfProcessingAttempts > maxAttemptsPerMessage)
                        .Select(i => i.Id)
                        .ToList();

                    ids.AddRange(idsToPrune);
                }

                using (var session = store.OpenAsyncSession())
                {
                    foreach (var id in ids)
                    {
                        var documentId = FailedMessage.MakeDocumentId(id);
                        var message = await session.LoadAsync<FailedMessage>(documentId);

                        log.Info("Processing: {0} truncating {1} processed attempts ", id, message.ProcessingAttempts.Count);

                        message.ProcessingAttempts = message.ProcessingAttempts
                            .OrderByDescending(pa => pa.AttemptedAt)
                            .Take(maxAttemptsPerMessage)
                            .ToList();
                    }

                    await session.SaveChangesAsync().ConfigureAwait(false);
                }
            }
        }

        static void ConfigureLogging()
        {
            var config = new LoggingConfiguration();
            var consoleTarget = new ConsoleTarget
            {
                Name = "console",
                Layout = "${longdate}|${level:uppercase=true}|${logger}|${message}",
            };
            config.AddRule(LogLevel.Info, LogLevel.Fatal, consoleTarget, "*");

            LogManager.Configuration = config;
        }

        static Logger log = LogManager.GetCurrentClassLogger();
    }
}
