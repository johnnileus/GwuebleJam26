using Godot;
using System;
using System.Diagnostics;
using System.Drawing;
using Color = Godot.Color;

public enum CellState : byte{
    Unburnt,
    Burning,
    Burnt
}

public struct Cell{
    public CellState State;
    public float Fuel;
    public float Moisture;
    public float BurnTimer;
}


public class FireChunk{
     public Cell[] Current;
     public Cell[] Next;
     public Vector2I ChunkPosition;
     

     public FireChunk(int size, Vector2I chunkPos){
         Current = new Cell[size * size];
         Next = new Cell[size * size];
         ChunkPosition = chunkPos;
     }
}

public partial class FireManager : Node{

    [Export] private float _cellSize;
    [Export] private int _chunkSize, _chunksW, _chunksH;

    [Export] private float _baseSpreadChance = .002f;
    [Export] private float _minBurnDurationToSpread = 2f;
    [Export] private float _fuelBurnRate = 2f;
    [Export] private float _maxBurnDuration = 15f;
    [Export] private float _startingFuel = 25f;
    
    private FireChunk[] _chunks;

    public override void _Ready(){
        
        _chunks = new FireChunk[_chunksW * _chunksH];

        for (int i = 0; i < _chunks.Length; i++) {
            Vector2I chunkPos = new Vector2I(i % _chunksW, i / _chunksW);
            _chunks[i] = new FireChunk(_chunkSize, chunkPos);
        }

        for (int i = 0; i < _chunks.Length; i++) {
            var curChunk = _chunks[i];
            for (int j = 0; j < _chunks[i].Current.Length; j++) {
                curChunk.Current[j].State = CellState.Unburnt;
                curChunk.Current[j].Fuel = _startingFuel;
                curChunk.Current[j].Moisture = 0f;
            }
        }

        _chunks[0].Current[0].State = CellState.Burning;
    }


    public override void _Process(double delta){
        var t1 = Time.GetTicksUsec();
        CalculateTick(delta);
        var t2 = Time.GetTicksUsec();
        // GD.Print($"took {(t2-t1)/1000f}ms");
        DrawGrid();
    }
    
    public int GetChunkIndex(int x, int y) {
        int wrappedX = ((x % _chunkSize) + _chunkSize) % _chunkSize;
        int wrappedY = ((y % _chunkSize) + _chunkSize) % _chunkSize;
        return wrappedY * _chunkSize + wrappedX;
    }
    private void CalculateTick(double delta){
        foreach (var chunk in _chunks) {
            for (int y = 0; y < _chunkSize; y++) {
                for (int x = 0; x < _chunkSize; x++) {
                    chunk.Next[GetChunkIndex(x, y)] = CalculateCell(chunk, x, y, delta);
                }
            }
        }

        foreach (var chunk in _chunks) {
            (chunk.Current, chunk.Next) = (chunk.Next, chunk.Current);
        }



    }

    private Cell CalculateCell(FireChunk chunk, int x, int y, double delta){
        Cell cell = chunk.Current[GetChunkIndex(x, y)];

        switch (cell.State) {
            case CellState.Unburnt:
                return TryIgnite(chunk, x, y, cell, delta);
            case CellState.Burning:
                cell.BurnTimer += (float)delta;
                cell.Fuel -= _fuelBurnRate * (float)delta;

                if (cell.Fuel <= 0f || cell.BurnTimer >= _maxBurnDuration)
                {
                    cell.State = CellState.Burnt;
                    cell.Fuel = 0f;
                }
                return cell;
            case CellState.Burnt:
            default:
                return cell;
        }

    }
    
    private Cell TryIgnite(FireChunk chunk, int x, int y, Cell cell, double delta){
        if (cell.Fuel <= 0f) return cell;

        for (int i = -1; i <= 1; i++) {
            for (int j = -1; j <= 1; j++) {
                if (i == 0 && j == 0) continue;

                int nx = x + j;
                int ny = y + i;
                if (nx < 0 || nx >= _chunkSize || ny < 0 || ny >= _chunkSize) continue;

                Cell neighbor = chunk.Current[GetChunkIndex(nx, ny)];
                if (neighbor.State != CellState.Burning || neighbor.BurnTimer < _minBurnDurationToSpread) continue;

                if (GD.Randf() < ComputeSpreadChance(j, i, cell, delta)) {
                    cell.State = CellState.Burning;
                    return cell;
                }
            }
        }

        return cell;
    }
    
    private float ComputeSpreadChance(int dx, int dy, Cell target, double delta){
        float chance = _baseSpreadChance * (float) delta;
        GD.Print(chance);
        
        bool isDiagonal = dx != 0 && dy != 0;
        if (isDiagonal) chance *= 0.7f;

        chance *= 1f - Mathf.Clamp(target.Moisture, 0f, 1f);

        return Mathf.Clamp(chance, 0f, 1f);
    }
    
    private void DrawGrid(){
        float chunkWidth = _chunkSize * _cellSize;
        foreach (var chunk in _chunks) {
            Vector3 offset = new Vector3(chunk.ChunkPosition[0] * chunkWidth, 0, chunk.ChunkPosition[1] * chunkWidth);

            for (int y = 0; y < _chunkSize; y++) {
                for (int x = 0; x < _chunkSize; x++) {
                    Cell cell = chunk.Current[GetChunkIndex(x, y)];

                    Vector3 position = new Vector3(x * _cellSize, 0,  y * _cellSize);
                    Vector3 size = new Vector3(_cellSize, 0.01f,_cellSize);
                    Color colour = StateToColour(cell);

                    DrawCell(position + offset, size * .9f, colour);
                }
            }
        }
        
    }
    private Color StateToColour(Cell cell) => cell.State switch
    {
        CellState.Unburnt  => new Color(0.15f, 0.5f + cell.Fuel / 24f, 0.1f),
        CellState.Burning  => Colors.OrangeRed.Lerp(Colors.Yellow, cell.Fuel / _startingFuel),
        CellState.Burnt => new Color(0.12f, 0.1f, 0.1f), _ => Colors.Magenta
    };

    private void DrawCell(Vector3 pos, Vector3 size, Color col){
        
        DebugDraw3D.DrawBox(pos, Quaternion.Identity, size, col);
        

    }
    
    
}
