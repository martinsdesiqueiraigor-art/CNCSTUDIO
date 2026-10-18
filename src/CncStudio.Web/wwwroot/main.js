// Inicializa o runtime .NET (WebAssembly) e roda o Casillas Code
import { dotnet } from './_framework/dotnet.js'

const runtime = await dotnet
    .withDiagnosticTracing(false)
    .withApplicationArgumentsFromQuery()
    .create();

const config = runtime.getConfig();
await runtime.runMain(config.mainAssemblyName, [globalThis.location.href]);
