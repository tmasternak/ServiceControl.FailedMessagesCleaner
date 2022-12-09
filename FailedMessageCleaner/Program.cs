using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NLog;
using NLog.Config;
using NLog.Targets;
using Raven.Client.Document;
using Raven.Client.Embedded;
using Raven.Database.Storage.Esent.Debug;
using ServiceControl.MessageFailures;

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

            Console.WriteLine("ONLY RUN THIS WHILE SERVICECONTROL IS NOT RUNNING");

            Console.WriteLine("Type YES if ServiceControl is NOT running and if registered as a Windows service is set to DISABLED to prevent it from starting due to system service recovery features.");

            Console.Write("Input: ");
            var result = Console.ReadLine();

            if (result != "YES")
            {
                Console.WriteLine($"Exiting as result was not equal to YES but '{result}'");
                return;
            }

            var path = args[0]; // @"C:\ProgramData\Particular\ServiceControl\Particular.Rabbitmq\DB";

            log.Info($"Connecting to RavenDB instance at {path} ...");

            try
            {
                using var store = RavenBootstrapper.Setup(path, 33334);
#pragma warning disable CS0618 // Type or member is obsolete
                store.Conventions.DefaultQueryingConsistency = ConsistencyOptions.AlwaysWaitForNonStaleResultsAsOfLastWrite;
#pragma warning restore CS0618 // Type or member is obsolete
                store.Initialize();

                log.Info($"Connected. Processing FailedMessage documents ...");

                await CleanFailedMessages(store).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                log.Error($"""
                    An unexpected error occured: {ex.Message} ({ex.GetType()}) [Exception hash: {CreateMD5(ex.ToString())}]
                    
                    Please run again!
                    
                    If the error persists try to repair the database as follows:

                        esentutl / r RVN / l "logs"
                        esentutl / p Data

                    Contact Particular Software at support@particular.net for assistance.
                    """);
            }
            log.Info($"Clean-up finished press <any key> to exit ...");
            Console.ReadLine();
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

                using (var session = store.OpenSession())
                {
                    var messages = session
                        .Query<FailedMessage>()
                        .Where(x => x.ProcessingAttempts.Count > maxAttemptsPerMessage)
                        .Take(pageSize)
                        .ToList();

                    start += messages.Count;

                    if (messages.Count == 0)
                    {
                        log.Info("Scanned {0:N0} documents.", start);
                        return;
                    }

                    foreach (var message in messages)
                    {
                        log.Info("Processing: {0} truncating {1:N0} processed attempts ", message.UniqueMessageId, message.ProcessingAttempts.Count);

                        message.ProcessingAttempts = message.ProcessingAttempts
                            .OrderByDescending(pa => pa.AttemptedAt)
                            .Take(maxAttemptsPerMessage)
                            .ToList();
                    }

                    session.SaveChanges();
                }
            }
        }
        static void ConfigureLogging()
        {
            var config = new LoggingConfiguration();
            var consoleTarget = new ConsoleTarget
            {
                Name = "console",
                Layout = "${processtime}|${level:uppercase=true}|${message}",
            };

            config.AddRule(LogLevel.Info, LogLevel.Fatal, consoleTarget, "*");

            LogManager.Configuration = config;
        }

        static string CreateMD5(string input)
        {
            // Use input string to calculate MD5 hash
            using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] inputBytes = System.Text.Encoding.ASCII.GetBytes(input);
                byte[] hashBytes = md5.ComputeHash(inputBytes);

                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < hashBytes.Length; i++)
                {
                    sb.Append(hashBytes[i].ToString("X2"));
                }
                return sb.ToString();
            }
        }
        static Logger log = LogManager.GetCurrentClassLogger();
    }
}
