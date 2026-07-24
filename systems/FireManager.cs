using Godot;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Drawing;
using Godot.Collections;
using Array = System.Array;
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
     public bool IsOnFire = false;


     public FireChunk(int size, Vector2I chunkPos){
         Current = new Cell[size * size];
         Next = new Cell[size * size];
         ChunkPosition = chunkPos;
     }
}

public partial class FireManager : Node{

    [Export] private bool _drawGrid = true;
    
    [Export] private float _cellSize;
    [Export] private int _interiorSize, _gridW, _gridH;

    [Export] private float _baseSpreadChance = .3f;
    [Export] private float _minBurnDurationToSpread = 2f;
    [Export] private float _fuelBurnRate = 2f;
    [Export] private float _startingFuel = 25f;

    private readonly float _hexRowOffset = Mathf.Sin(Mathf.Pi / 3f);
    
    private FireChunk[] _chunks;
    private List<FireChunk> _activeChunks;
    private Cell[] _padded;

    private FastNoiseLite _noise = new();
    [Export] private float _moistureScale = 1.5f;

    private int _totalTicks = 0;
    

    
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


        
        _chunks = new FireChunk[_gridW * _gridH];
        

        _activeChunks = new List<FireChunk>();

        for (int i = 0; i < _chunks.Length; i++) {
            Vector2I chunkPos = new Vector2I(i % _gridW, i / _gridW);
            _chunks[i] = new FireChunk(_interiorSize, chunkPos);
        }

        _padded = new Cell[(_interiorSize + 2) * (_interiorSize + 2)];

        
        foreach (var chunk in _chunks){
            for (int y = 0; y < _interiorSize; y++){
                for (int x = 0; x < _interiorSize; x++){
                    int gx = chunk.ChunkPosition.X * _interiorSize + x;
                    int gy = chunk.ChunkPosition.Y * _interiorSize + y;
                    float moisture = (_noise.GetNoise2D(gx*2f, gy*2f) + 1f) * 0.5f;

                    chunk.Current[y * _interiorSize + x] = new Cell {
                        State = CellState.Unburnt,
                        Fuel = _startingFuel,
                        Moisture = moisture * _moistureScale,
                    };
                }
            }
        }

        _chunks[0].Current[0].State = CellState.Burning;
        _chunks[0].IsOnFire = true;
        

    }
    
    public override void _Process(double delta){
        var t1 = Time.GetTicksUsec();
        CalculateTick(delta);
        var t2 = Time.GetTicksUsec();
        GD.Print($"took {(t2-t1)/1000f}ms");

        if (_drawGrid)
            DrawGrid();
    }
    
    
    private FireChunk GetChunk(int cx, int cy){
        return cx < 0 || cx >= _gridW || cy < 0 || cy >= _gridH ? null : _chunks[cy * _gridW + cx];
    }

        
    
    public int GetChunkIndex(int x, int y) {
        int wrappedX = ((x % _interiorSize) + _interiorSize) % _interiorSize;
        int wrappedY = ((y % _interiorSize) + _interiorSize) % _interiorSize;
        return wrappedY * _interiorSize + wrappedX;
    }
    
    private Cell SampleWorld(Vector2I chunkPos, int x, int y){
        int cx = chunkPos.X,
            cy = chunkPos.Y,
            lx = x,
            ly = y;
        if (x < 0) { cx--; lx = _interiorSize - 1; }
        else if (x >= _interiorSize) { cx++; lx = 0; }
        if (y < 0) { cy--; ly = _interiorSize - 1; }
        else if (y >= _interiorSize) { cy++; ly = 0; }

        FireChunk n = GetChunk(cx, cy);
        return n != null ? n.Current[ly * _interiorSize + lx] : default; 
    }
    
    private void FillPadded(FireChunk chunk){

        for (int y = 0; y < _interiorSize; y++)
            Array.Copy(chunk.Current, y * _interiorSize, _padded, PaddedIdx(0, y), _interiorSize);

        for (int x = -1; x <= _interiorSize; x++){
            _padded[PaddedIdx(x, -1)]         = SampleWorld(chunk.ChunkPosition, x, -1);
            _padded[PaddedIdx(x, _interiorSize)] = SampleWorld(chunk.ChunkPosition, x, _interiorSize);
        }

        for (int y = 0; y < _interiorSize; y++){
            _padded[PaddedIdx(-1, y)]         = SampleWorld(chunk.ChunkPosition, -1, y);
            _padded[PaddedIdx(_interiorSize, y)] = SampleWorld(chunk.ChunkPosition, _interiorSize, y);
        }
    }
    
    private int PaddedIdx(int x, int y) => (y + 1) * (_interiorSize + 2) + (x + 1);
    
    private void CalculateTick(double delta){



        _activeChunks.Clear();
        foreach (var chunk in _chunks) {
            if (NeighboursHaveFire(chunk)) _activeChunks.Add(chunk);
        }
        
        foreach (var chunk in _activeChunks) {
            FillPadded(chunk);
            bool hasFire = false;
            for (int y = 0; y < _interiorSize; y++) {
                for (int x = 0; x < _interiorSize; x++) {
                    Cell cell = CalculateCell(_padded, x, y, delta);
                    chunk.Next[y * _interiorSize + x] = cell;
                    hasFire |= cell.State == CellState.Burning;
                }
            }
        
            chunk.IsOnFire = hasFire;
        }
        foreach (var chunk in _activeChunks)               
            (chunk.Current, chunk.Next) = (chunk.Next, chunk.Current);
        

        _totalTicks++;
        GD.Print($"Ticks elapsed: {_totalTicks}");
    }



    private Cell CalculateCell(Cell[] chunk, int x, int y, double delta){
        Cell cell = chunk[PaddedIdx(x, y)];

        switch (cell.State) {
            case CellState.Unburnt:
                return TryIgnite(chunk, x, y, cell, delta);
            case CellState.Burning:
                cell.BurnTimer += (float)delta;
                cell.Fuel -= _fuelBurnRate * (float)delta;

                if (cell.Fuel <= 0f)
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
    
    private bool NeighboursHaveFire(FireChunk chunk){
        if (chunk.IsOnFire) return true;
        Vector2I p = chunk.ChunkPosition;
        for (int dy = -1; dy <= 1; dy++){
            for (int dx = -1; dx <= 1; dx++){
                if (dx == 0 && dy == 0) continue;
                FireChunk n = GetChunk(p.X + dx, p.Y + dy);
                if (n != null && n.IsOnFire) return true;
            }
        }
        return false;
    }
    
    private float ComputeSpreadChance(Cell target, double delta){
        float chance = _baseSpreadChance * (float) delta;
        chance *= 1f - Mathf.Clamp(target.Moisture, 0f, 1f);
        return Mathf.Clamp(chance, 0f, 1f);
    }
    
    private void DrawGrid(){
        float chunkWidth = _interiorSize * _cellSize;
        foreach (var chunk in _chunks) {
            Vector3 offset = new Vector3(chunk.ChunkPosition[0] * chunkWidth, 0, chunk.ChunkPosition[1] * chunkWidth * Mathf.Sin(Mathf.Pi/3f));

            for (int y = 0; y < _interiorSize; y++) {
                for (int x = 0; x < _interiorSize; x++) {
                    Cell cell = chunk.Current[GetChunkIndex(x, y)];

                    Vector3 position = new Vector3(y % 2 == 0 ? x * _cellSize : x * _cellSize + _cellSize/2f, 0,  y * _cellSize * _hexRowOffset);
                    Vector3 size = new Vector3(_cellSize, 0.01f,_cellSize);
                    Color colour = GetColour(cell);

                    DrawCell(position + offset, size * .9f, colour);
                }
            }
        }
        
    }
    private Color GetColour(Cell cell) => cell.State switch
    {
        CellState.Unburnt  => new Color(0.15f, 0.2f + cell.Moisture, 0.1f),
        CellState.Burning  => Colors.OrangeRed.Lerp(Colors.Yellow, cell.Fuel / _startingFuel),
        CellState.Burnt => new Color(0.12f, 0.1f, 0.1f), _ => Colors.Magenta
    };

    private void DrawCell(Vector3 pos, Vector3 size, Color col){
        
        DebugDraw3D.DrawBox(pos, Quaternion.Identity, size, col);
        

    }

    
}
