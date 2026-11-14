namespace TickHappens.Engine.Spatial;

/// <summary>
/// Uniform grid over a fixed world rectangle, rebuilt every tick with a counting sort (O(n), no allocations).
/// Used for crowd separation, contact damage and area queries over thousands of entities.
/// </summary>
public sealed class SpatialGrid
{
    private readonly float _cell, _minX, _minY;
    private readonly int _cols, _rows;
    private readonly int[] _cellStart;
    private int[] _cellOf = [];
    private int[] _items = [];

    public SpatialGrid(float minX, float minY, float width, float height, float cellSize)
    {
        _minX = minX;
        _minY = minY;
        _cell = cellSize;
        _cols = Math.Max(1, (int)MathF.Ceiling(width / cellSize));
        _rows = Math.Max(1, (int)MathF.Ceiling(height / cellSize));
        _cellStart = new int[_cols * _rows + 1];
    }

    private int CellX(float x) => Math.Clamp((int)((x - _minX) / _cell), 0, _cols - 1);
    private int CellY(float y) => Math.Clamp((int)((y - _minY) / _cell), 0, _rows - 1);

    public void Build(ReadOnlySpan<float> xs, ReadOnlySpan<float> ys)
    {
        int n = xs.Length;
        if (_cellOf.Length < n) { _cellOf = new int[n * 2]; _items = new int[n * 2]; }
        Array.Clear(_cellStart);
        for (int i = 0; i < n; i++)
        {
            int c = CellY(ys[i]) * _cols + CellX(xs[i]);
            _cellOf[i] = c;
            _cellStart[c + 1]++;
        }
        for (int c = 0; c < _cols * _rows; c++) _cellStart[c + 1] += _cellStart[c];
        Span<int> fill = _cols * _rows <= 16384 ? stackalloc int[_cols * _rows] : new int[_cols * _rows];
        for (int i = 0; i < n; i++)
        {
            int c = _cellOf[i];
            _items[_cellStart[c] + fill[c]++] = i;
        }
    }

    /// <summary>Calls visit(index) for every item in cells overlapping the circle's bounding box.</summary>
    public void Query<TVisitor>(float x, float y, float radius, ref TVisitor visitor) where TVisitor : struct, IGridVisitor
    {
        int x0 = CellX(x - radius), x1 = CellX(x + radius), y0 = CellY(y - radius), y1 = CellY(y + radius);
        for (int cy = y0; cy <= y1; cy++)
            for (int cx = x0; cx <= x1; cx++)
            {
                int c = cy * _cols + cx;
                for (int k = _cellStart[c]; k < _cellStart[c + 1]; k++) visitor.Visit(_items[k]);
            }
    }
}

public interface IGridVisitor
{
    void Visit(int index);
}
