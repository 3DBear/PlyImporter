# PlyImporter
PLY (Polygon File Format) importer for Unity

## Installation
Put both .cs files into your project

## Usage
Call GetVerticesAndTriangles in PlyHandler. This will give you at minimum a list of vertices and triangle indices, optionally a list of colors if vertex colors are present

## Features

- Support for Binary Little Endian formatting with vertex colors
- Support for Ascii formatting

## Planned features

- Support for Binary Big Endian
- Support for vertex colors for Ascii formatting
