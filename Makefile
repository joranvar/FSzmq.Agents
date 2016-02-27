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
include $(MAKE_utilsDir)/FSharp.mk

vpath %.fs src

# Assemblies
Unit.dll = $(call FSHARP_mkDllTarget,test/Unit.dll)

# Test assemblies
UNITTEST = $(call NUNIT_mkTestTarget,$(Unit.dll))

# Dependencies
$(Unit.dll): test/Unit.fs

.PHONY: all
all: $(UNITTEST)

.PHONY: clean
clean: cleanall
