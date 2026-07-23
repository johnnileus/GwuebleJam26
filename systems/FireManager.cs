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

    [Export] private bool _useGpu = true;
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
    
    //gpu
    private RenderingDevice _rd;
    private RDShaderFile _shaderFile;
    private Rid _shader, _pipeline, _buffer, _uniformSet;
    private int _bufferCellCount; 
    private float[] _gpuScratch;
    private byte[] _stagingBytes;
    
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
        
        InitialiseGpu();

    }
    
    public override void _Process(double delta){
        var t1 = Time.GetTicksUsec();
        CalculateTick(delta);
        var t2 = Time.GetTicksUsec();
        GD.Print($"took {(t2-t1)/1000f}ms");

        if (_drawGrid)
            DrawGrid();
    }

    private void InitialiseGpu(){
        _rd = RenderingServer.CreateLocalRenderingDevice();
        _shaderFile = GD.Load<RDShaderFile>("res://systems/FireCalculator.glsl");
        _shader = _rd.ShaderCreateFromSpirV(_shaderFile.GetSpirV());
        _pipeline = _rd.ComputePipelineCreate(_shader);
        
        // one buffer holding every cell of every chunk
        _bufferCellCount = _chunks.Length * _interiorSize * _interiorSize;
        _gpuScratch = new float[_bufferCellCount * 4];
        _stagingBytes = new byte[_gpuScratch.Length * sizeof(float)];

        var byteLen = (uint)(_gpuScratch.Length * sizeof(float));
        _buffer = _rd.StorageBufferCreate(byteLen);   // allocate empty, fill per tick

        var uniform = new RDUniform {
            UniformType = RenderingDevice.UniformType.StorageBuffer,
            Binding = 0
        };
        uniform.AddId(_buffer);
        _uniformSet = _rd.UniformSetCreate(new Array<RDUniform> { uniform }, _shader, 0);
    }

    public void GlslTest(FireChunk chunk){

        // Prepare our data. We use floats in the shader, so we need 32 bit.
        Cell[] src = chunk.Current;
        float[] input = new float[src.Length * 4];

        for (int i = 0; i < src.Length; i++) {
            int b = i * 4;
            input[b + 0] = (float)src[i].State;
            input[b + 1] = src[i].Fuel;
            input[b + 2] = src[i].Moisture;
            input[b + 3] = src[i].BurnTimer;
            GD.Print($"before: {src[i].Fuel}");
        }
        
        var inputBytes = new byte[input.Length * sizeof(float)];
        Buffer.BlockCopy(input, 0, inputBytes, 0, inputBytes.Length);
        
        // Create a storage buffer that can hold our float values.
        // Each float has 4 bytes (32 bit) so 10 x 4 = 40 bytes
        var buffer = _rd.StorageBufferCreate((uint)inputBytes.Length, inputBytes);
        
        // Create a uniform to assign the buffer to the rendering device
        var uniform = new RDUniform {
            UniformType = RenderingDevice.UniformType.StorageBuffer,
            Binding = 0
        };
        
        uniform.AddId(buffer);
        var uniformSet = _rd.UniformSetCreate(new Array<RDUniform> { uniform }, _shader, 0);
        
        // Create a compute pipeline
        var pipeline = _rd.ComputePipelineCreate(_shader);
        var computeList = _rd.ComputeListBegin();
        _rd.ComputeListBindComputePipeline(computeList, pipeline);
        _rd.ComputeListBindUniformSet(computeList, uniformSet, 0);
        uint groups = (uint) Mathf.CeilToInt(src.Length / 64f);
        _rd.ComputeListDispatch(computeList, xGroups: groups, yGroups: 1, zGroups: 1);
        _rd.ComputeListEnd();
        
        // Submit to GPU and wait for sync
        _rd.Submit();
        _rd.Sync();
        
        // Read back the data from the buffers
        var outputBytes = _rd.BufferGetData(buffer);
        var output = new float[input.Length];
        Buffer.BlockCopy(outputBytes, 0, output, 0, outputBytes.Length);

        for (int i = 0; i < src.Length; i++) {
            int b = i * 4;
            chunk.Next[i] = new Cell {
                State = (CellState)(byte)(output[b + 0]),
                Fuel = output[b + 1],
                Moisture = output[b + 2],
                BurnTimer = output[b + 3],
            };
            GD.Print($"after:{chunk.Next[i].Fuel}");
        }

        _rd.FreeRid(buffer);

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

        if (_useGpu) {
            CalculateGpuTick();
            foreach (var chunk in _chunks)               
                (chunk.Current, chunk.Next) = (chunk.Next, chunk.Current);
        }
        else {
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
        }

        _totalTicks++;
        GD.Print($"Ticks elapsed: {_totalTicks}");
    }

    private void CalculateGpuTick()
    {
        
        
        int cellsPerChunk = _interiorSize * _interiorSize;

        // pack all chunks into the scratch array
        for (int c = 0; c < _chunks.Length; c++) {
            Cell[] src = _chunks[c].Current;
            int baseCell = c * cellsPerChunk;
            for (int i = 0; i < src.Length; i++) {
                int b = (baseCell + i) * 4;
                _gpuScratch[b + 0] = (float)src[i].State;
                _gpuScratch[b + 1] = src[i].Fuel;
                _gpuScratch[b + 2] = src[i].Moisture;
                _gpuScratch[b + 3] = src[i].BurnTimer;
            }
        }

        // upload into the existing buffer (no realloc)
        Buffer.BlockCopy(_gpuScratch, 0, _stagingBytes, 0, _stagingBytes.Length);
        _rd.BufferUpdate(_buffer, 0, (uint)_stagingBytes.Length, _stagingBytes);

        // dispatch once for every cell
        long list = _rd.ComputeListBegin();
        _rd.ComputeListBindComputePipeline(list, _pipeline);
        _rd.ComputeListBindUniformSet(list, _uniformSet, 0);
        uint groups = (uint)Mathf.CeilToInt(_bufferCellCount / 64f);
        _rd.ComputeListDispatch(list, groups, 1, 1);
        _rd.ComputeListEnd();

        _rd.Submit();
        _rd.Sync();

        // read back
        var outBytes = _rd.BufferGetData(_buffer);
        Buffer.BlockCopy(outBytes, 0, _gpuScratch, 0, outBytes.Length);

        for (int c = 0; c < _chunks.Length; c++) {
            Cell[] dst = _chunks[c].Next;
            int baseCell = c * cellsPerChunk;
            for (int i = 0; i < dst.Length; i++) {
                int b = (baseCell + i) * 4;
                dst[i] = new Cell {
                    State     = (CellState)(byte)_gpuScratch[b + 0],
                    Fuel      = _gpuScratch[b + 1],
                    Moisture  = _gpuScratch[b + 2],
                    BurnTimer = _gpuScratch[b + 3],
                };
            }
        }
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
