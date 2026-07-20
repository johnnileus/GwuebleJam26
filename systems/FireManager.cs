using Godot;
using System;
using System.Collections.Generic;
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
     public bool Active = false;


     public FireChunk(int size, Vector2I chunkPos){
         Current = new Cell[size * size];
         Next = new Cell[size * size];
         ChunkPosition = chunkPos;
     }
}

public partial class FireManager : Node{

    [Export] private float _cellSize;
    [Export] private int _chunkSize, _chunksW, _chunksH;

    [Export] private float _baseSpreadChance = .3f;
    [Export] private float _minBurnDurationToSpread = 2f;
    [Export] private float _fuelBurnRate = 2f;
    [Export] private float _maxBurnDuration = 15f;
    [Export] private float _startingFuel = 25f;

    private  float hexRowOffset = Mathf.Sin(Mathf.Pi / 3f);
    
    private FireChunk[] _chunks;
    private List<FireChunk> _activeChunks;

    private Cell[] _padded;
    
    private static readonly Vector2I[] _evenNeighbors = { // clockwise starting NW
            new(-1, -1),  new(0, -1),
        new(-1, 0),           new(+1, 0),
            new(-1, +1),  new(0, +1)
    };
    private static readonly Vector2I[] _oddNeighbors = {
            new(0, -1),  new(+1, -1),
        new(-1, 0),           new(+1, 0),
            new(0, +1),  new(+1, +1)
    };

    public override void _Ready(){
        
        _chunks = new FireChunk[_chunksW * _chunksH];

        for (int i = 0; i < _chunks.Length; i++) {
            Vector2I chunkPos = new Vector2I(i % _chunksW, i / _chunksW);
            _chunks[i] = new FireChunk(_chunkSize, chunkPos);
        }

        _padded = new Cell[(_chunkSize + 2) * (_chunkSize + 2)];

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
        GD.Print($"took {(t2-t1)/1000f}ms");
        DrawGrid();
    }
    
    private FireChunk GetChunk(int cx, int cy){
        return cx < 0 || cx >= _chunksW || cy < 0 || cy >= _chunksH ? null : _chunks[cy * _chunksW + cx];
    }

        
    
    public int GetChunkIndex(int x, int y) {
        int wrappedX = ((x % _chunkSize) + _chunkSize) % _chunkSize;
        int wrappedY = ((y % _chunkSize) + _chunkSize) % _chunkSize;
        return wrappedY * _chunkSize + wrappedX;
    }
    
    private Cell SampleWorld(Vector2I chunkPos, int x, int y){
        int cx = chunkPos.X,
            cy = chunkPos.Y,
            lx = x,
            ly = y;
        if (x < 0) { cx--; lx = _chunkSize - 1; }
        else if (x >= _chunkSize) { cx++; lx = 0; }
        if (y < 0) { cy--; ly = _chunkSize - 1; }
        else if (y >= _chunkSize) { cy++; ly = 0; }

        FireChunk n = GetChunk(cx, cy);
        return n != null ? n.Current[ly * _chunkSize + lx] : default; 
    }
    
    private void FillPadded(FireChunk chunk){

        for (int y = 0; y < _chunkSize; y++)
            Array.Copy(chunk.Current, y * _chunkSize, _padded, PaddedIdx(0, y), _chunkSize);

        for (int x = -1; x <= _chunkSize; x++){
            _padded[PaddedIdx(x, -1)]         = SampleWorld(chunk.ChunkPosition, x, -1);
            _padded[PaddedIdx(x, _chunkSize)] = SampleWorld(chunk.ChunkPosition, x, _chunkSize);
        }

        for (int y = 0; y < _chunkSize; y++){
            _padded[PaddedIdx(-1, y)]         = SampleWorld(chunk.ChunkPosition, -1, y);
            _padded[PaddedIdx(_chunkSize, y)] = SampleWorld(chunk.ChunkPosition, _chunkSize, y);
        }
    }
    
    private int PaddedIdx(int x, int y) => (y + 1) * (_chunkSize + 2) + (x + 1);
    
    private void CalculateTick(double delta){
        foreach (var chunk in _chunks){
            FillPadded(chunk);                   
            for (int y = 0; y < _chunkSize; y++)
            for (int x = 0; x < _chunkSize; x++)
                chunk.Next[y * _chunkSize + x] = CalculateCell(_padded, x, y, delta);
        }

        foreach (var chunk in _chunks)               
            (chunk.Current, chunk.Next) = (chunk.Next, chunk.Current);
    }
    
    

    private Cell CalculateCell(Cell[] chunk, int x, int y, double delta){
        Cell cell = chunk[PaddedIdx(x, y)];

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
    
    private Cell TryIgnite(Cell[] chunk, int x, int y, Cell cell, double delta){
        if (cell.Fuel <= 0f) return cell;
        
        var offsets = y % 2 == 0 ? _evenNeighbors : _oddNeighbors;

        for (int k = 0; k < 6; k++) {
            
            Cell neighbor = chunk[PaddedIdx(x + offsets[k].X, y + offsets[k].Y)];
            if (neighbor.State != CellState.Burning || neighbor.BurnTimer < _minBurnDurationToSpread) continue;

            if (GD.Randf() < ComputeSpreadChance(cell, delta)) {
                cell.State = CellState.Burning;
                return cell;
            
            }
        }

        return cell;
    }
    
    private float ComputeSpreadChance(Cell target, double delta){
        float chance = _baseSpreadChance * (float) delta;
        chance *= 1f - Mathf.Clamp(target.Moisture, 0f, 1f);
        return Mathf.Clamp(chance, 0f, 1f);
    }
    
    private void DrawGrid(){
        float chunkWidth = _chunkSize * _cellSize;
        foreach (var chunk in _chunks) {
            Vector3 offset = new Vector3(chunk.ChunkPosition[0] * chunkWidth, 0, chunk.ChunkPosition[1] * chunkWidth * Mathf.Sin(Mathf.Pi/3f));

            for (int y = 0; y < _chunkSize; y++) {
                for (int x = 0; x < _chunkSize; x++) {
                    Cell cell = chunk.Current[GetChunkIndex(x, y)];

                    Vector3 position = new Vector3(y % 2 == 0 ? x * _cellSize : x * _cellSize + _cellSize/2f, 0,  y * _cellSize * hexRowOffset);
                    Vector3 size = new Vector3(_cellSize, 0.01f,_cellSize);
                    Color colour = StateToColour(cell);

                    DrawCell(position + offset, size * .9f, colour);
                }
            }
        }
        
    }
    private Color StateToColour(Cell cell) => cell.State switch
    {
        CellState.Unburnt  => new Color(0.15f, 0.5f, 0.1f),
        CellState.Burning  => Colors.OrangeRed.Lerp(Colors.Yellow, cell.Fuel / _startingFuel),
        CellState.Burnt => new Color(0.12f, 0.1f, 0.1f), _ => Colors.Magenta
    };

    private void DrawCell(Vector3 pos, Vector3 size, Color col){
        
        DebugDraw3D.DrawBox(pos, Quaternion.Identity, size, col);
        

    }
    
    
}
