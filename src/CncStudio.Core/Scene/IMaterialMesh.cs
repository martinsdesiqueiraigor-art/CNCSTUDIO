using CncStudio.Core.Rendering;

namespace CncStudio.Core.Scene;

/// <summary>Malha do material da fresa em blocos (grade de alturas ou voxels): a cena só precisa disto.</summary>
public interface IMaterialMesh
{
    int TileCount { get; }

    /// <summary>Malha do bloco de índice dado (vazia antes do primeiro Update).</summary>
    SolidMesh Tile(int index);

    /// <summary>Refaz os blocos que mudaram desde a última chamada e devolve os índices deles.</summary>
    IReadOnlyList<int> Update();

    /// <summary>Cores em uso. Trocar refaz todos os blocos no próximo Update.</summary>
    ScenePalette Palette { get; set; }
}
