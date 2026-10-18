// Inicializa o runtime .NET (WebAssembly) e roda o Casillas Code
import { dotnet } from './_framework/dotnet.js'

const runtime = await dotnet
    .withDiagnosticTracing(false)
    .withApplicationArgumentsFromQuery()
    .create();

const config = runtime.getConfig();
// "poc3d" no endereço (ou a marca global) abre a tela de teste do 3D da Fase 1
const args = [globalThis.location.href];
if (globalThis.CASILLAS_POC3D) args.push('poc3d');
await runtime.runMain(config.mainAssemblyName, args);
