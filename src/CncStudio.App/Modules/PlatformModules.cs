namespace CncStudio.App.Modules;

/// <summary>Um módulo da plataforma Casillas e se já existe no desktop.</summary>
public sealed record PlatformModule(string Id, string Name, string Description, bool Available);

/// <summary>Lista única dos módulos (barra lateral e tela Início). Ordem do plano PLAT-06.</summary>
public static class PlatformModules
{
    public static readonly IReadOnlyList<PlatformModule> All =
    [
        new("code", "Casillas Code", "Editor e simulador de programas Fanuc e Siemens, fresa e torno.", true),
        new("otimizador", "Casillas Otimizador", "Acha repetições em programas de CAM e propõe laços Macro B só para simulador.", false),
        new("macro", "Casillas Macro", "Geradores de programa com prévia 3D.", false),
        new("cam", "Casillas CAM", "Do desenho ao programa 2,5D.", false),
        new("academy", "Casillas Academy", "Cursos de programação CNC.", false),
    ];
}
