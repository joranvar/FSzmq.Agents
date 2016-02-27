.PHONY: default
default: all

MAKE_toolsDir ?= tools
MAKE_binDir   ?= bin
MAKE_objDir   ?= obj
MAKE_utilsDir ?= Makefiles

NUGET_nugetDir ?= lib/NuGet
include $(MAKE_utilsDir)/NuGet.mk
NUNIT_testDir ?= test
include $(MAKE_utilsDir)/NUnit.mk
FSHARP_flags  ?= -g --warnaserror -O --resident
include $(MAKE_utilsDir)/FSharp.mk

vpath %.fs src

# Assemblies
Tests.dll = $(call FSHARP_mkDllTarget,test/Tests.dll)
FSzmq.dll = $(call FSHARP_mkDllTarget,FSzmq.dll)

# NuGets
FsCheck = $(call NUGET_mkNuGetContentsTarget,FsCheck,lib/net45/FsCheck.dll)
fszmq = $(call NUGET_mkNuGetContentsTarget,fszmq,lib/net40/fszmq.dll)

# Test assemblies
UNITTEST = $(call NUNIT_mkTestTarget,$(Tests.dll))

# Dependencies
$(Tests.dll): test/Tests.fs $(FsCheck) $(FSzmq.dll) $(fszmq) /nix/store/6p5jbgy54yanvx22m75c1xan4gn2y3b9-zeromq-4.1.4/lib/libzmq.so
$(FSzmq.dll): FSzmq.fs $(fszmq) /nix/store/6p5jbgy54yanvx22m75c1xan4gn2y3b9-zeromq-4.1.4/lib/libzmq.so


.PHONY: all
all: $(UNITTEST)

.PHONY: clean
clean: cleanall
