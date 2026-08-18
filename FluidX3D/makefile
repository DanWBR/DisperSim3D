MAKEFLAGS = -j$(nproc)
CC = g++
# -fPIC is required for the disp-bridge-* shared-library targets and is a
# no-op (or near-no-op) cost for the bin/FluidX3D executable build, so it's
# in the global CFLAGS to keep one set of object files usable by both.
CFLAGS = -std=c++17 -pthread -O -Wno-comment -fPIC

.PHONY: no-target
no-target:
	@echo "\033[91mError\033[0m: Please select one of these targets: make Linux-X11, make Linux, make macOS, make Android, make disp-bridge-Linux, make disp-bridge-macOS"

# NOTE: src/opencl.hpp does `#include <OpenCL/include/CL/opencl.hpp>`, which
# requires ./src to be on the include path (then the include resolves to
# ./src/OpenCL/include/CL/opencl.hpp). The Windows MSVC build adds ./src via
# the project's IncludePath property; mirroring that here makes the Unix
# build pick up the same bundled OpenCL headers.
Linux-X11 Linux macOS Android disp-bridge-Linux disp-bridge-macOS: LDFLAGS_OPENCL = -I./src -I./src/OpenCL/include

Linux-X11 Linux disp-bridge-Linux: LDLIBS_OPENCL = -L./src/OpenCL/lib -lOpenCL
macOS disp-bridge-macOS: LDLIBS_OPENCL = -framework OpenCL
Android: LDLIBS_OPENCL = -L/system/vendor/lib64 -lOpenCL

Linux-X11: LDFLAGS_X11 = -I./src/X11/include
macOS: LDFLAGS_X11 = -I/opt/X11/include
Linux Android disp-bridge-Linux disp-bridge-macOS: LDFLAGS_X11 =

Linux-X11: LDLIBS_X11 = -L./src/X11/lib -lX11 -lXrandr
macOS: LDLIBS_X11 = -L/opt/X11/lib -lX11 -lXrandr
Linux Android disp-bridge-Linux disp-bridge-macOS: LDLIBS_X11 =

Linux-X11 Linux macOS Android: bin/FluidX3D

# ── DisperSim 3D shared-library target ──────────────────────────────────────
# Builds bin/libFluidX3D.so (Linux) / bin/libFluidX3D.dylib (macOS) from the
# same sources as bin/FluidX3D MINUS main.cpp PLUS disp_bridge.cpp. The
# resulting library exposes the fx3d_* C ABI that DisperSim3D.csproj consumes
# via [DllImport("FluidX3D")] — no extension, so .NET picks the platform's
# native naming convention: FluidX3D.dll on Windows, libFluidX3D.so on
# Linux, libFluidX3D.dylib on macOS.
disp-bridge-Linux:  bin/libFluidX3D.so
disp-bridge-macOS:  bin/libFluidX3D.dylib

DISP_OBJS = temp/disp_bridge.o temp/tracer_bridge.o temp/graphics.o temp/info.o \
            temp/kernel.o temp/lbm.o temp/lodepng.o temp/setup.o temp/shapes.o

bin/libFluidX3D.so: $(DISP_OBJS) make.sh
	@mkdir -p bin
	$(CC) -shared -fPIC $(DISP_OBJS) -o bin/libFluidX3D.so $(CFLAGS) \
	    $(LDFLAGS_OPENCL) $(LDLIBS_OPENCL) -Wl,-rpath,'$$ORIGIN'

bin/libFluidX3D.dylib: $(DISP_OBJS) make.sh
	@mkdir -p bin
	$(CC) -dynamiclib -fPIC $(DISP_OBJS) -o bin/libFluidX3D.dylib $(CFLAGS) \
	    $(LDFLAGS_OPENCL) $(LDLIBS_OPENCL)

temp/disp_bridge.o: src/disp_bridge.cpp src/disp_bridge.h src/defines.hpp src/lbm.hpp src/units.hpp src/opencl.hpp src/utilities.hpp make.sh
	@mkdir -p temp
	$(CC) -c -fPIC src/disp_bridge.cpp -o temp/disp_bridge.o $(CFLAGS) $(LDFLAGS_OPENCL)

# tracer_bridge.cpp holds the fx3d_tracer_* C ABI (GPU port of
# BuoyantTracerEngine). The Windows vcxproj compiles it next to
# disp_bridge.cpp; without it here the Unix .so/.dylib loads but every
# fx3d_tracer_* P/Invoke throws EntryPointNotFoundException.
temp/tracer_bridge.o: src/tracer_bridge.cpp src/disp_bridge.h src/defines.hpp src/lbm.hpp src/units.hpp src/opencl.hpp src/utilities.hpp make.sh
	@mkdir -p temp
	$(CC) -c -fPIC src/tracer_bridge.cpp -o temp/tracer_bridge.o $(CFLAGS) $(LDFLAGS_OPENCL)



bin/FluidX3D: temp/graphics.o temp/info.o temp/kernel.o temp/lbm.o temp/lodepng.o temp/main.o temp/setup.o temp/shapes.o make.sh
	@mkdir -p bin
	$(CC) temp/*.o -o bin/FluidX3D $(CFLAGS) $(LDFLAGS_OPENCL) $(LDLIBS_OPENCL) $(LDFLAGS_X11) $(LDLIBS_X11)

temp/graphics.o: src/graphics.cpp src/defines.hpp src/graphics.hpp src/lodepng.hpp src/utilities.hpp make.sh
	@mkdir -p temp
	$(CC) -c src/graphics.cpp -o temp/graphics.o $(CFLAGS) $(LDFLAGS_X11)

temp/info.o: src/info.cpp src/defines.hpp src/graphics.hpp src/info.hpp src/lbm.hpp src/lodepng.hpp src/opencl.hpp src/units.hpp src/utilities.hpp make.sh
	@mkdir -p temp
	$(CC) -c src/info.cpp -o temp/info.o $(CFLAGS) $(LDFLAGS_OPENCL)

temp/kernel.o: src/kernel.cpp src/kernel.hpp src/lodepng.hpp src/utilities.hpp
	@mkdir -p temp
	$(CC) -c src/kernel.cpp -o temp/kernel.o $(CFLAGS)

temp/lbm.o: src/lbm.cpp src/defines.hpp src/graphics.hpp src/info.hpp src/lbm.hpp src/lodepng.hpp src/opencl.hpp src/units.hpp src/utilities.hpp make.sh
	@mkdir -p temp
	$(CC) -c src/lbm.cpp -o temp/lbm.o $(CFLAGS) $(LDFLAGS_OPENCL)

temp/lodepng.o: src/lodepng.cpp src/lodepng.hpp
	@mkdir -p temp
	$(CC) -c src/lodepng.cpp -o temp/lodepng.o $(CFLAGS)

temp/main.o: src/main.cpp src/defines.hpp src/graphics.hpp src/info.hpp src/lbm.hpp src/lodepng.hpp src/opencl.hpp src/setup.hpp src/shapes.hpp src/units.hpp src/utilities.hpp make.sh
	@mkdir -p temp
	$(CC) -c src/main.cpp -o temp/main.o $(CFLAGS) $(LDFLAGS_OPENCL)

temp/setup.o: src/setup.cpp src/defines.hpp src/graphics.hpp src/info.hpp src/lbm.hpp src/lodepng.hpp src/opencl.hpp src/setup.hpp src/shapes.hpp src/units.hpp src/utilities.hpp make.sh
	@mkdir -p temp
	$(CC) -c src/setup.cpp -o temp/setup.o $(CFLAGS) $(LDFLAGS_OPENCL)

temp/shapes.o: src/shapes.cpp src/shapes.hpp src/utilities.hpp make.sh
	@mkdir -p temp
	$(CC) -c src/shapes.cpp -o temp/shapes.o $(CFLAGS) $(LDFLAGS_OPENCL)

.PHONY: clean
clean:
	@rm -rf temp bin/FluidX3D bin/libFluidX3D.so bin/libFluidX3D.dylib
