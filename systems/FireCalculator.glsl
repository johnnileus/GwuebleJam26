#[compute]
#version 450

// Invocations in the (x, y, z) dimension
layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;

struct Cell {
    float state;
    float fuel;
    float moisture;
    float burnTimer;
};

// A binding to the buffer we create in our script
layout(set = 0, binding = 0, std430) restrict buffer MyDataBuffer {
    Cell cells[];
} 
cell_buffer;

// The code we want to execute in each invocation
void main() {
    // gl_GlobalInvocationID.x uniquely identifies this invocation across all work groups
    uint i = gl_GlobalInvocationID.x;
    if (i >= cell_buffer.cells.length()) return;

    if (cell_buffer.cells[i].state == 1.0) {
        cell_buffer.cells[i].fuel -= .1;
    }
}