using System;
using System.ComponentModel.Composition.Hosting;
using System.IO;
using NLog;
using Raven.Client.Embedded;

namespace FailedMessageCleaner
{
    class RavenBootstrapper
    {
        public static string ReadAllTextWithoutLocking(string path)
        {
            using (var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var textReader = new StreamReader(fileStream))
            {
                return textReader.ReadToEnd();
            }
        }

        public static string ReadLicense()
        {
            using (var resourceStream = typeof(RavenBootstrapper).Assembly.GetManifestResourceStream("FailedMessageCleaner.RavenLicense.xml"))
            using (var reader = new StreamReader(resourceStream))
            {
                return reader.ReadToEnd();
            }
        }

        public static EmbeddableDocumentStore Setup(string dbPath, int dbPort)
        {
            var documentStore = new EmbeddableDocumentStore();

            documentStore.DataDirectory = dbPath;
            documentStore.Configuration.CompiledIndexCacheDirectory = dbPath;

            documentStore.UseEmbeddedHttpServer = false;
            documentStore.EnlistInDistributedTransactions = false;

            var localRavenLicense = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RavenLicense.xml");
            if (File.Exists(localRavenLicense))
            {
                log.Info($"Loading RavenDB license found from {localRavenLicense}");
                documentStore.Configuration.Settings["Raven/License"] = ReadAllTextWithoutLocking(localRavenLicense);
            }
            else
            {
                log.Info("Loading Embedded RavenDB license");
                documentStore.Configuration.Settings["Raven/License"] = ReadLicense();
            }

            //This is affects only remote access to the database in maintenace mode and enables access without authentication
            documentStore.Configuration.Settings["Raven/AnonymousAccess"] = "Admin";
            documentStore.Configuration.Settings["Raven/Licensing/AllowAdminAnonymousAccessForCommercialUse"] = "true";

            documentStore.Configuration.DisableClusterDiscovery = true;
            documentStore.Configuration.ResetIndexOnUncleanShutdown = true;
            documentStore.Configuration.Port = dbPort;
            documentStore.Configuration.HostName = "localhost";
            documentStore.Conventions.SaveEnumsAsIntegers = true;

            documentStore.Configuration.Catalog.Catalogs.Add(new AssemblyCatalog(typeof(RavenBootstrapper).Assembly));

            return documentStore;
        }

        static Logger log = LogManager.GetCurrentClassLogger();
    }
}