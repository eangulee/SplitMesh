using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine.Events;
namespace SplitMesh
{
    /// <summary>
    /// 模型切割脚本
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshCollider))]
    public class SplitObject : MonoBehaviour
    {
        public bool fill = true;
        public Rect uvRange = Rect.MinMaxRect(0, 0, 1, 1);
        public MeshInfo MeshInfo { get; set; }
        void Start()
        {
            if (MeshInfo == null)
            {
                MeshInfo = new MeshInfo(GetComponent<MeshFilter>().sharedMesh);
            }
        }
        /// <summary>
        /// 更新网格信息
        /// </summary>
        /// <param name="info"></param>
        public void UpdateMesh(params MeshInfo[] info)
        {
            CombineInstance[] coms = new CombineInstance[info.Length];
            for (int i = 0; i < info.Length; i++)
            {
                coms[i].mesh = info[i].GetMesh();
                coms[i].transform = Matrix4x4.identity;
            }
            Mesh mesh = new Mesh();
            mesh.CombineMeshes(coms);
            mesh.RecalculateBounds();
            //mesh.RecalculateNormals();
            GetComponent<MeshFilter>().mesh = mesh;
            GetComponent<MeshCollider>().sharedMesh = mesh;
            MeshInfo = new MeshInfo(mesh);
            MeshInfo.center = info[0].center;
            MeshInfo.size = info[0].size;
        }

        /// <summary>
        /// 根据平面切割
        /// </summary>
        /// <param name="plane"></param>
        public void Split(Plane plane)
        {
            //原点转换到对象空间?????
            Vector3 point = transform.InverseTransformPoint(plane.normal * -plane.distance);
            //法线转换到对象空间
            Vector3 normal = transform.InverseTransformDirection(plane.normal);
            //处理缩放
            normal.Scale(transform.localScale);
            //归一化
            normal.Normalize();
            //新建两个新的mesh对象
            MeshInfo a = new MeshInfo();
            MeshInfo b = new MeshInfo();
            //保存顶点在切割平面的上方还是下方，用bool数组保存
            bool[] above = new bool[MeshInfo.vertices.Count];
            int[] newTriangles = new int[MeshInfo.vertices.Count];
            //分组保存顶点
            for (int i = 0; i < newTriangles.Length; i++)
            {
                Vector3 vert = MeshInfo.vertices[i];
                //通过计算顶点与原点所成向量与法线的点积来判断是否在切割平的上方，点积大于在上方，否则在下方
                above[i] = Vector3.Dot(vert - point, normal) >= 0f;
                //上方和下方顶点分组保存
                if (above[i])
                {
                    newTriangles[i] = a.vertices.Count;
                    a.Add(vert, MeshInfo.uvs[i], MeshInfo.normals[i], MeshInfo.tangents[i]);
                }
                else
                {
                    newTriangles[i] = b.vertices.Count;
                    b.Add(vert, MeshInfo.uvs[i], MeshInfo.normals[i], MeshInfo.tangents[i]);
                }
            }

            //计算生成切面顶点
            List<Vector3> cutPoint = new List<Vector3>();
            int triangleCount = MeshInfo.triangles.Count / 3;
            for (int i = 0; i < triangleCount; i++)
            {
                int _i0 = MeshInfo.triangles[i * 3];
                int _i1 = MeshInfo.triangles[i * 3 + 1];
                int _i2 = MeshInfo.triangles[i * 3 + 2];

                bool _a0 = above[_i0];
                bool _a1 = above[_i1];
                bool _a2 = above[_i2];
                //三个点都在上方，直接保存顶点索引
                if (_a0 && _a1 && _a2)
                {
                    a.triangles.Add(newTriangles[_i0]);
                    a.triangles.Add(newTriangles[_i1]);
                    a.triangles.Add(newTriangles[_i2]);
                }
                //三个点都在下方，直接保存顶点索引
                else if (!_a0 && !_a1 && !_a2)
                {
                    b.triangles.Add(newTriangles[_i0]);
                    b.triangles.Add(newTriangles[_i1]);
                    b.triangles.Add(newTriangles[_i2]);
                }
                else
                {
                    int up, down0, down1;
                    //索引0在一方，其他两个索引在另一方
                    if (_a1 == _a2 && _a0 != _a1)
                    {
                        up = _i0;
                        down0 = _i1;
                        down1 = _i2;
                    }
                    //索引1在一方，其他两个索引在另一方
                    else if (_a2 == _a0 && _a1 != _a2)
                    {
                        up = _i1;
                        down0 = _i2;
                        down1 = _i0;
                    }
                    //索引2在一方，其他两个索引在另一方
                    else
                    {
                        up = _i2;
                        down0 = _i0;
                        down1 = _i1;
                    }
                    Vector3 pos0, pos1;
                    //根据3个索引所处的方位，分割并重建三角形索引
                    if (above[up])
                        SplitTriangle(a, b, point, normal, newTriangles, up, down0, down1, out pos0, out pos1);
                    else
                        SplitTriangle(b, a, point, normal, newTriangles, up, down0, down1, out pos1, out pos0);
                    cutPoint.Add(pos0);
                    cutPoint.Add(pos1);
                }
            }
            //合并顶点并设置mesh信息
            a.CombineVertices(0.001f);
            a.center = MeshInfo.center;
            a.size = MeshInfo.size;
            b.CombineVertices(0.001f);
            b.center = MeshInfo.center;
            b.size = MeshInfo.size;

            //是否填充切面
            if (fill && cutPoint.Count > 2)
            {
                //生成切面
                MeshInfo cut = FastFillCutEdges(cutPoint, point, normal);
                //合并切面
                Instantiate(gameObject).GetComponent<SplitObject>().UpdateMesh(b, cut);
                //切面翻转
                cut.Reverse();
                //合并切面
                Instantiate(gameObject).GetComponent<SplitObject>().UpdateMesh(a, cut);
            }
            else
            {
                Instantiate(gameObject).GetComponent<SplitObject>().UpdateMesh(a);
                Instantiate(gameObject).GetComponent<SplitObject>().UpdateMesh(b);
            }
            //销毁自身
            Destroy(gameObject);
        }

        /// <summary>
        /// 分割三角形
        /// </summary>
        /// <param name="top"></param>
        /// <param name="bottom"></param>
        /// <param name="point"></param>
        /// <param name="normal"></param>
        /// <param name="newTriangles"></param>
        /// <param name="up"></param>
        /// <param name="down0"></param>
        /// <param name="down1"></param>
        /// <param name="pos0"></param>
        /// <param name="pos1"></param>
        void SplitTriangle(MeshInfo top, MeshInfo bottom, Vector3 point, Vector3 normal, int[] newTriangles, int up, int down0, int down1, out Vector3 pos0, out Vector3 pos1)
        {
            Vector3 v0 = MeshInfo.vertices[up];
            Vector3 v1 = MeshInfo.vertices[down0];
            Vector3 v2 = MeshInfo.vertices[down1];
            //计算世界空间原点与v0所成向量与法线夹角的点积，dot(a,b) = a·b = |a||b|cos∠(a, b)
            float topDot = Vector3.Dot(point - v0, normal);
            //已知point为世界空间原点，法线normal为n，设point - v0 = a,v1-v0 = b,则dot(a,n) /dot(b,n) =  a·n / b·n = 
            float aScale = Mathf.Clamp01(topDot / Vector3.Dot(v1 - v0, normal));
            float bScale = Mathf.Clamp01(topDot / Vector3.Dot(v2 - v0, normal));
            Vector3 pos_a = v0 + (v1 - v0) * aScale;
            Vector3 pos_b = v0 + (v2 - v0) * bScale;

            Vector2 u0 = MeshInfo.uvs[up];
            Vector2 u1 = MeshInfo.uvs[down0];
            Vector2 u2 = MeshInfo.uvs[down1];
            Vector3 uv_a = (u0 + (u1 - u0) * aScale);
            Vector3 uv_b = (u0 + (u2 - u0) * bScale);

            Vector3 n0 = MeshInfo.normals[up];
            Vector3 n1 = MeshInfo.normals[down0];
            Vector3 n2 = MeshInfo.normals[down1];
            Vector3 normal_a = (n0 + (n1 - n0) * aScale).normalized;
            Vector3 normal_b = (n0 + (n2 - n0) * bScale).normalized;

            Vector4 t0 = MeshInfo.tangents[up];
            Vector4 t1 = MeshInfo.tangents[down0];
            Vector4 t2 = MeshInfo.tangents[down1];
            Vector4 tangent_a = (t0 + (t1 - t0) * aScale).normalized;
            Vector4 tangent_b = (t0 + (t2 - t0) * bScale).normalized;
            tangent_a.w = t1.w;
            tangent_b.w = t2.w;

            int top_a = top.vertices.Count;
            top.Add(pos_a, uv_a, normal_a, tangent_a);
            int top_b = top.vertices.Count;
            top.Add(pos_b, uv_b, normal_b, tangent_b);
            top.triangles.Add(newTriangles[up]);
            top.triangles.Add(top_a);
            top.triangles.Add(top_b);

            int down_a = bottom.vertices.Count;
            bottom.Add(pos_a, uv_a, normal_a, tangent_a);
            int down_b = bottom.vertices.Count;
            bottom.Add(pos_b, uv_b, normal_b, tangent_b);

            bottom.triangles.Add(newTriangles[down0]);
            bottom.triangles.Add(newTriangles[down1]);
            bottom.triangles.Add(down_b);

            bottom.triangles.Add(newTriangles[down0]);
            bottom.triangles.Add(down_b);
            bottom.triangles.Add(down_a);

            pos0 = pos_a;
            pos1 = pos_b;
        }

        /// <summary>
        /// 根据切点生成切面
        /// </summary>
        /// <param name="edges"></param>
        /// <param name="pos"></param>
        /// <param name="normal"></param>
        /// <returns></returns>
        MeshInfo FillCutEdges(List<Vector3> edges, Vector3 pos, Vector3 normal)
        {
            if (edges.Count < 3)
                throw new Exception("edges point less 3!");

            for (int i = 0; i < edges.Count; i++)
            {
                for (int j = i + 1; j < edges.Count; j++)
                    if ((edges[i] - edges[j]).sqrMagnitude < 1e-5f)
                    {
                        edges.RemoveAt(j);
                    }
            }

            Vector3 start = edges[0];
            Vector3 dir = Vector3.zero;
            for (int i = 1; i < edges.Count; i++)
            {
                if (dir == Vector3.zero)
                {
                    dir = edges[i] - start;
                }
            }
            dir.Normalize();
            int count = edges.Count - 1;
            for (int i = 2; i < count; i++)
            {
                Vector3 a = edges[i] - start;
                float angle = Vector3.Dot(a.normalized, dir);
                float dis = a.sqrMagnitude;
                if (dis < 1e-6f)
                    continue;

                bool change = false;

                for (int j = i + 1; j < edges.Count; j++)
                {
                    Vector3 b = edges[j] - start;
                    float _angle = Vector3.Dot(b.normalized, dir);
                    float _dis = b.sqrMagnitude;
                    bool next = _dis <= dis;
                    next = (angle - _angle < 0.001f) && (_angle > 0.9999f ? _dis < dis : _dis >= dis);
                    next = change ? false : next;
                    if (_angle - angle > 0.001f || next || _dis < 1e-6f)
                    {
                        if (_angle - angle > 0.001f)
                            change = true;
                        angle = _angle;
                        dis = _dis;
                        Vector3 temp;
                        temp = edges[i];
                        edges[i] = edges[j];
                        edges[j] = temp;
                    }
                }
            }

            Vector4 tangent = MeshInfo.CalculateTangent(normal);

            MeshInfo cutEdges = new MeshInfo();
            for (int i = 0; i < edges.Count; i++)
                cutEdges.Add(edges[i], Vector2.zero, normal, tangent);
            for (int i = 1; i < count; i++)
            {
                cutEdges.triangles.Add(0);
                cutEdges.triangles.Add(i);
                cutEdges.triangles.Add(i + 1);
            }

            cutEdges.center = MeshInfo.center;
            cutEdges.size = MeshInfo.size;
            cutEdges.MapperCube(uvRange);
            return cutEdges;
        }

        /// <summary>
        /// 根据切点快速生成切面
        /// </summary>
        /// <param name="edges"></param>
        /// <param name="pos"></param>
        /// <param name="normal"></param>
        /// <returns></returns>
        MeshInfo FastFillCutEdges(List<Vector3> edges, Vector3 pos, Vector3 normal)
        {
            if (edges.Count < 3)
                throw new Exception("edges point less 3!");

            for (int i = 0; i < edges.Count - 3; i++)
            {
                Vector3 t = edges[i + 1];
                Vector3 temp = edges[i + 3];
                for (int j = i + 2; j < edges.Count - 1; j += 2)
                {
                    if ((edges[j] - t).sqrMagnitude < 1e-6)
                    {
                        edges[j] = edges[i + 2];
                        edges[i + 3] = edges[j + 1];
                        edges[j + 1] = temp;
                        break;
                    }
                    if ((edges[j + 1] - t).sqrMagnitude < 1e-6)
                    {
                        edges[j + 1] = edges[i + 2];
                        edges[i + 3] = edges[j];
                        edges[j] = temp;
                        break;
                    }
                }
                edges.RemoveAt(i + 2);
            }
            edges.RemoveAt(edges.Count - 1);

            Vector4 tangent = MeshInfo.CalculateTangent(normal);

            MeshInfo cutEdges = new MeshInfo();
            for (int i = 0; i < edges.Count; i++)
                cutEdges.Add(edges[i], Vector2.zero, normal, tangent);
            int count = edges.Count - 1;
            for (int i = 1; i < count; i++)
            {
                cutEdges.triangles.Add(0);
                cutEdges.triangles.Add(i);
                cutEdges.triangles.Add(i + 1);
            }

            cutEdges.center = MeshInfo.center;
            cutEdges.size = MeshInfo.size;
            cutEdges.MapperCube(uvRange);
            return cutEdges;
        }
    }
}