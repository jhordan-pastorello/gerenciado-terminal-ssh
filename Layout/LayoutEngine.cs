namespace StorageTerm.Layout;

public readonly record struct PaneTile(int Row, int Column, int RowSpan, int ColumnSpan);

/// <summary>
/// Calcula o mosaico dos terminais. A grade lógica é 6x2 para permitir todos os
/// layouts com proporções exatas, sem reconstruir definições de Grid.
///
/// 1: inteiro
/// 2: 50/50 lado a lado
/// 3: T1 em cima e T2 embaixo à esquerda; T3 (mais novo) ocupa a direita inteira
/// 4: 2x2
/// 5: 2x2 + o mais novo ocupando a coluna extra inteira
/// 6: 3x2
/// </summary>
public static class LayoutEngine
{
    public const int GridColumns = 6;
    public const int GridRows = 2;
    public const int MaxPanes = 6;

    public static IReadOnlyList<PaneTile> Compute(int count) => count switch
    {
        1 => new[]
        {
            new PaneTile(0, 0, 2, 6)
        },
        2 => new[]
        {
            new PaneTile(0, 0, 2, 3),
            new PaneTile(0, 3, 2, 3)
        },
        3 => new[]
        {
            new PaneTile(0, 0, 1, 3),
            new PaneTile(1, 0, 1, 3),
            new PaneTile(0, 3, 2, 3)
        },
        4 => new[]
        {
            new PaneTile(0, 0, 1, 3),
            new PaneTile(0, 3, 1, 3),
            new PaneTile(1, 0, 1, 3),
            new PaneTile(1, 3, 1, 3)
        },
        5 => new[]
        {
            new PaneTile(0, 0, 1, 2),
            new PaneTile(0, 2, 1, 2),
            new PaneTile(1, 0, 1, 2),
            new PaneTile(1, 2, 1, 2),
            new PaneTile(0, 4, 2, 2)
        },
        6 => new[]
        {
            new PaneTile(0, 0, 1, 2),
            new PaneTile(0, 2, 1, 2),
            new PaneTile(0, 4, 1, 2),
            new PaneTile(1, 0, 1, 2),
            new PaneTile(1, 2, 1, 2),
            new PaneTile(1, 4, 1, 2)
        },
        _ => Array.Empty<PaneTile>()
    };
}
