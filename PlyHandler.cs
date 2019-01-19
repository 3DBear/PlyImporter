using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using System.Linq;
using System;
using System.Globalization;
using System.Text;

namespace ThreeDeeBear.Models.Ply
{
    public class PlyResult
    {
        public List<Vector3> Vertices;
        public List<int> Triangles;
        public List<Color> Colors;

        public PlyResult(List<Vector3> vertices, List<int> triangles, List<Color> colors)
        {
            Vertices = vertices;
            Triangles = triangles;
            Colors = colors;
        }
    }
    public static class PlyHandler
    {
        #region Ascii

        private static PlyResult ParseAscii(List<string> plyFile, PlyHeader header)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var colors = new List<Color>();
            var headerEndIndex = plyFile.IndexOf("end_header");
            var vertexStartIndex = headerEndIndex + 1;
            var faceStartIndex = vertexStartIndex + header.VertexCount + 1;
            plyFile.GetRange(vertexStartIndex, header.VertexCount).ForEach(vertex =>
            {
                var xyzrgb = vertex.Split(' ');
                vertices.Add(ParseVertex(xyzrgb, header));
                colors.Add(ParseColor(xyzrgb, header));
            });

            plyFile.GetRange(faceStartIndex, header.FaceCount).ForEach(face =>
            {
                triangles.AddRange(GetTriangles(face, header));
            });
            return new PlyResult(vertices, triangles, colors);
        }
        private static Vector3 ParseVertex(string[] xyzrgb, PlyHeader header)
        {
            decimal dx, dy, dz;
            decimal.TryParse(xyzrgb[header.XIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out dx);
            decimal.TryParse(xyzrgb[header.YIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out dy);
            decimal.TryParse(xyzrgb[header.ZIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out dz);
            return new Vector3((float)dx, (float)dy, (float)dz);
        }

        private static Color ParseColor(string[] xyzrgb, PlyHeader header)
        {
            int r = 255;
            int g = 255;
            int b = 255;
            int a = 255;
            if (header.RedIndex.HasValue)
                int.TryParse(xyzrgb[header.RedIndex.Value], NumberStyles.Integer, CultureInfo.InvariantCulture, out r);
            if (header.GreenIndex.HasValue)
                int.TryParse(xyzrgb[header.GreenIndex.Value], NumberStyles.Integer, CultureInfo.InvariantCulture, out g);
            if (header.BlueIndex.HasValue)
                int.TryParse(xyzrgb[header.BlueIndex.Value], NumberStyles.Integer, CultureInfo.InvariantCulture, out b);
            if (header.AlphaIndex.HasValue)
                int.TryParse(xyzrgb[header.AlphaIndex.Value], NumberStyles.Integer, CultureInfo.InvariantCulture, out a);
            return new Color(r / 255f, g / 255f, b / 255f, a / 255f);
        }

        private static List<int> GetTriangles(string faceVertexList, PlyHeader header)
        {
            switch (header.FaceParseMode)
            {
                case PlyFaceParseMode.VertexCountVertexIndex:
                    var split = faceVertexList.Split(' ');
                    var count = Convert.ToInt32(split.First());
                    switch (count)
                    {
                        case 3: // triangle
                            return split.ToList().GetRange(1, 3).Select(x => Convert.ToInt32(x)).ToList();
                        case 4: // face
                            var triangles = new List<int>();
                            var indices = split.ToList().GetRange(1, 4).Select(x => Convert.ToInt32(x)).ToList();
                            triangles.AddRange(QuadToTriangles(indices));
                            return triangles;
                        default:
                            Debug.LogWarning("Warning: Found a face with more than 4 vertices, skipping...");
                            return new List<int>();
                    }
                default:
                    Debug.LogWarning("Ply GetTriangles: Unknown parse mode");
                    return new List<int>();
            }
        }
        #endregion

        #region Binary

        private static PlyResult ParseBinaryLittleEndian(string path, PlyHeader header)
        {
            var headerAsText = header.RawHeader.Aggregate((a, b) => $"{a}\n{b}") + "\n";
            var headerAsBytes = Encoding.ASCII.GetBytes(headerAsText);
            var withoutHeader = File.ReadAllBytes(path).Skip(headerAsBytes.Length).ToArray();
            var colors = new List<Color>();
            var vertices = GetVertices(withoutHeader, header, out colors);
            var triangles = GetTriangles(withoutHeader, header);
            return new PlyResult(vertices, triangles, colors);
        }

        private static List<Vector3> GetVertices(byte[] bytes, PlyHeader header, out List<Color> colors)
        {
            var vertices = new List<Vector3>();
            colors = new List<Color>();
            int bpvc = 4; // bytes per vertex component
            int bpcc = 1; // bytes per color component
            bool hasColor = header.RedIndex.HasValue && header.GreenIndex.HasValue && header.BlueIndex.HasValue;
            // todo: support other types than just float for vertex components and byte for color components
            int bytesPerVertex = GetByteCountPerVertex(header);

            for (int i = 0; i < header.VertexCount; i++)
            {
                int byteIndex = i * bytesPerVertex;
                var x = System.BitConverter.ToSingle(bytes.SubArray(byteIndex + 0 * bpvc, bpvc), 0);
                var y = System.BitConverter.ToSingle(bytes.SubArray(byteIndex + 1 * bpvc, bpvc), 0);
                var z = System.BitConverter.ToSingle(bytes.SubArray(byteIndex + 2 * bpvc, bpvc), 0);
                if (hasColor)
                {
                    byte r, g, b, a = 255;
                    r = bytes[byteIndex + 3 * bpvc + 0 * bpcc];
                    g = bytes[byteIndex + 3 * bpvc + 1 * bpcc];
                    b = bytes[byteIndex + 3 * bpvc + 2 * bpcc];
                    if (header.AlphaIndex.HasValue)
                    {
                        a = bytes[byteIndex + 3 * bpvc + 3 * bpcc];
                    }
                    colors.Add(new Color(r/255f, g/255f, b/255f, a/255f));
                }


                vertices.Add(new Vector3(x, y, z));
            }
            return vertices;
        }
        private static List<int> GetTriangles(byte[] bytes, PlyHeader header)
        {
            var toSkip = header.VertexCount * GetByteCountPerVertex(header);
            var triangles = new List<int>();
            int facesRead = 0;
            int bytesRead = 0;
            int bytesPerTriangleIndex = 4;
            while (facesRead < header.FaceCount)
            {
                var faceIndex = toSkip + bytesRead;
                var indexCount = bytes[faceIndex];
                if (indexCount == 3)
                {
                    for (int i = 0; i < indexCount; i++)
                    {
                        triangles.Add(System.BitConverter.ToInt32(bytes.SubArray(faceIndex + 1 + i * bytesPerTriangleIndex, bytesPerTriangleIndex), 0));
                    }
                    bytesRead += 1 + indexCount * bytesPerTriangleIndex;
                }
                else if (indexCount == 4)
                {
                    var tmp = new List<int>();
                    for (int i = 0; i < indexCount; i++)
                    {
                        tmp.Add(System.BitConverter.ToInt32(bytes.SubArray(faceIndex + 1 + i * bytesPerTriangleIndex, bytesPerTriangleIndex), 0));
                    }
                    triangles.AddRange(QuadToTriangles(tmp));
                    bytesRead += 1 + indexCount * bytesPerTriangleIndex;
                }
                else
                {
                    Debug.LogWarning("Warning: Found a face with more than 4 vertices, skipping...");
                }

                facesRead++;
            }
            return triangles;
        }

        private static int GetByteCountPerVertex(PlyHeader header)
        {
            int bpvc = 4; // bytes per vertex component
            int bpcc = 1; // bytes per color component
            // todo: support other types than just float for vertex components and byte for color components
            int r = header.RedIndex.HasValue ? bpcc : 0;
            int g = header.GreenIndex.HasValue ? bpcc : 0;
            int b = header.BlueIndex.HasValue ? bpcc : 0;
            int a = header.AlphaIndex.HasValue ? bpcc : 0;
            return (3 * bpvc + r + g + b + a);
        }
        #endregion

        private static List<int> QuadToTriangles(List<int> quad)
        {
            return new List<int>() { quad[0], quad[1], quad[2], quad[2], quad[3], quad[0] };
        }


        public static PlyResult GetVerticesAndTriangles(string path)
        {
            List<string> header = File.ReadLines(path).TakeUntilIncluding(x => x == "end_header").ToList();
            var headerParsed = new PlyHeader(header);
            if (headerParsed.Format == PlyFormat.Ascii)
            {
                return ParseAscii(File.ReadAllLines(path).ToList(), headerParsed);
            }
            else if (headerParsed.Format == PlyFormat.BinaryLittleEndian)
            {
                return ParseBinaryLittleEndian(path, headerParsed);
            }
            else // todo: support BinaryBigEndian
            {
                return null;
            }
        }

    }
}