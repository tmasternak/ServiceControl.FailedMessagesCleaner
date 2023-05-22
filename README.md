# Overview

This is a tool to fix RavenDB 3.5 ServiceControl database issues caused by too big `FailedMessage` documents being stored.

The tool will trim problematic documents to make sure that the ingestion process can continue at expected performance level.

The problem solved by the tool [has been solved](https://github.com/Particular/ServiceControl/issues/2916) in the [4.28.3](https://github.com/Particular/ServiceControl/releases/tag/4.28.3) version of ServiceControl and does not affect newly created databases.
