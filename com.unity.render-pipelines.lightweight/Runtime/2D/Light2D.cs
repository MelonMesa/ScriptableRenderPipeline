using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.U2D.Shape;
using Unity.RenderPipeline2D.External.LibTessDotNet;
using Mesh = UnityEngine.Mesh;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UnityEngine.Experimental.Rendering.LWRP
{
    // TODO: 
    //     Fix parametric mesh code so that the vertices, triangle, and color arrays are only recreated when number of sides change
    //     Change code to update mesh only when it is on screen. Maybe we can recreate a changed mesh if it was on screen last update (in the update), and if it wasn't set it dirty. If dirty, in the OnBecameVisible function create the mesh and clear the dirty flag.
    [ExecuteAlways]
    public class Light2D : MonoBehaviour
    {
        private Mesh m_Mesh = null;

        public enum LightProjectionTypes
        {
            Shape = 0,
            Point = 1
        }

        public enum CookieStyles
        {
            Parametric = 0,
            //FreeForm=1,
            Sprite = 2,
        }

        private enum Light2DType
        {
            ShapeType0 = 0,
            ShapeType1,
            ShapeType2,
            Point,
            Count
        }

        public enum LightOperation
        {
            Type0 = 0,
            Type1 = 1,
            Type2 = 2
        }

        public enum ParametricShapes
        {
            Circle,
            Freeform,
        }

        public enum LightOverlapMode
        {
            Additive,
            AlphaBlend
        }

        public enum LightQuality
        {
            Fast,
            Accurate
        }

        static Material m_PointLightMaterial = null;
        static Material m_PointLightVolumeMaterial = null;
        static Material m_ShapeCookieSpriteAlphaBlendMaterial = null;
        static Material m_ShapeCookieSpriteAdditiveMaterial = null;
        static Material m_ShapeCookieSpriteVolumeMaterial = null;
        static Material m_ShapeVertexColoredAlphaBlendMaterial = null;
        static Material m_ShapeVertexColoredAdditiveMaterial = null;
        static Material m_ShapeVertexColoredVolumeMaterial = null;
        static CullingGroup m_CullingGroup;
        static List<Light2D>[] m_Lights = SetupLightArray();

        //------------------------------------------------------------------------------------------
        //                              Shared Light 
        //------------------------------------------------------------------------------------------

        [SerializeField]
        public LightProjectionTypes lightProjectionType
        {
            get { return m_LightProjectionType; }
            set { m_LightProjectionType = value; }
        }
        [SerializeField]
        private LightProjectionTypes m_LightProjectionType = LightProjectionTypes.Shape;
        private LightProjectionTypes m_PreviousLightProjectionType = LightProjectionTypes.Shape;

        public Color color
        {
            get { return m_LightColor; }
            set { m_LightColor = value; }
        }
        [ColorUsageAttribute(false, true)]
        [SerializeField]
        public Color m_LightColor = Color.white;
        private Color m_PreviousLightColor = Color.white;

        public Sprite lightCookieSprite
        {
            get { return m_LightCookieSprite; }
            set { m_LightCookieSprite = value; }
        }
        [SerializeField]
        private Sprite m_LightCookieSprite;
        private Sprite m_PreviousLightCookieSprite = null;

        public float volumeOpacity
        {
            get { return m_LightVolumeOpacity; }
            set { m_LightVolumeOpacity = value; }
        }
        [SerializeField]
        private float m_LightVolumeOpacity = 0.0f;
        private float m_PreviousLightVolumeOpacity = 0.0f;

        [SerializeField]
        [Serialization.FormerlySerializedAs("m_ShapeLightType")]
        private LightOperation m_LightOperation = LightOperation.Type0;
        private LightOperation m_PreviousLightOperation = LightOperation.Type0;

        //------------------------------------------------------------------------------------------
        //                              Values for Point light type
        //------------------------------------------------------------------------------------------
        public float pointLightInnerAngle
        {
            get { return m_PointLightInnerAngle; }
            set { m_PointLightInnerAngle = value; }
        }
        [SerializeField]
        private float m_PointLightInnerAngle = 360;

        public float pointLightOuterAngle
        {
            get { return m_PointLightOuterAngle; }
            set { m_PointLightOuterAngle = value; }
        }
        [SerializeField]
        private float m_PointLightOuterAngle = 360;

        public float pointLightInnerRadius
        {
            get { return m_PointLightInnerRadius; }
            set { m_PointLightInnerRadius = value; }
        }
        [SerializeField]
        private float m_PointLightInnerRadius = 0;

        public float pointLightOuterRadius
        {
            get { return m_PointLightOuterRadius; }
            set { m_PointLightOuterRadius = value; }
        }
        [SerializeField]
        private float m_PointLightOuterRadius = 1;

        public float pointLightDistance
        {
            get { return m_PointLightDistance; }
            set { m_PointLightDistance = value; }
        }
        [SerializeField]
        private float m_PointLightDistance = 3;

        public LightQuality pointLightQuality
        {
            get { return m_PointLightQuality; }
            set { m_PointLightQuality = value; }
        }
        [SerializeField]
        private LightQuality m_PointLightQuality = LightQuality.Fast;

        [SerializeField]
        int[] m_ApplyToSortingLayers = new int[1];     // These are sorting layer IDs.

        //------------------------------------------------------------------------------------------
        //                              Values for Shape light type
        //------------------------------------------------------------------------------------------
        public CookieStyles shapeLightCookieStyle
        {
            get { return m_ShapeLightCookieStyle; }
            set { m_ShapeLightCookieStyle = value; }
        }
        [SerializeField]
        [Serialization.FormerlySerializedAs("m_ShapeLightStyle")]
        private CookieStyles m_ShapeLightCookieStyle = CookieStyles.Parametric;

        public ParametricShapes shapeLightParametricShape
        {
            get { return m_ShapeLightParametricShape; }
            set { m_ShapeLightParametricShape = value; }
        }
        [SerializeField]
        [Serialization.FormerlySerializedAs("m_ParametricShape")]
        private ParametricShapes m_ShapeLightParametricShape = ParametricShapes.Circle; // This should be removed and fixed in the inspector

        public float shapeLightFeathering
        {
            get { return m_ShapeLightFeathering; }
            set { m_ShapeLightFeathering = value; }
        }
        [SerializeField]
        private float m_ShapeLightFeathering = 0.50f;
        private float m_PreviousShapeLightFeathering = -1;


        public int shapeLightParametricSides
        {
            get { return m_ShapeLightParametricSides; }
            set { m_ShapeLightParametricSides = value; }
        }
        [SerializeField]
        [Serialization.FormerlySerializedAs("m_ParametricSides")]
        private int m_ShapeLightParametricSides = 128;
        private int m_PreviousShapeLightParametricSides = -1;


        public Vector2 shapeLightOffset
        {
            get { return m_ShapeLightOffset; }
            set { m_ShapeLightOffset = value; }
        }
        [SerializeField]
        public Vector2 m_ShapeLightOffset;
        private Vector2 m_PreviousShapeLightOffset;


        [SerializeField]
        private int m_ShapeLightOrder = 0;
        private int m_PreviousShapeLightOrder = 0;

        [SerializeField]
        private LightOverlapMode m_ShapeLightOverlapMode = LightOverlapMode.Additive;
        //private BlendingModes m_PreviousShapeLightBlending = BlendingModes.Additive;

        private int m_LightCullingIndex = -1;
        private Bounds m_LocalBounds;

        public LightProjectionTypes GetLightProjectionType()
        {
            return m_LightProjectionType;
        }

        static public List<Light2D>[] SetupLightArray()
        {
            int numLightTypes = (int)Light2DType.Count;
            List<Light2D>[] retArray = new List<Light2D>[numLightTypes];
            for (int i = 0; i < numLightTypes; i++)
                retArray[i] = new List<Light2D>();

            return retArray;
        }

        public BoundingSphere GetBoundingSphere()
        {
            BoundingSphere boundingSphere = new BoundingSphere();
            if (m_LightProjectionType == LightProjectionTypes.Shape)
            {
                Vector3 maximum = transform.TransformPoint(m_LocalBounds.max);
                Vector3 minimum = transform.TransformPoint(m_LocalBounds.min);
                Vector3 center = 0.5f * (maximum + minimum);
                float radius = Vector3.Magnitude(maximum - center);

                boundingSphere.radius = radius;
                boundingSphere.position = center;
            }
            else
            {
                boundingSphere.radius = m_PointLightOuterRadius;
                boundingSphere.position = transform.position;
            }
            return boundingSphere;
        }

        static public void SetupCulling(Camera camera)
        {
            if (m_CullingGroup == null)
                return;

            m_CullingGroup.targetCamera = camera;

            int totalLights = 0;
            for (int lightTypeIndex = 0; lightTypeIndex < m_Lights.Length; lightTypeIndex++)
                totalLights += m_Lights[lightTypeIndex].Count;

            BoundingSphere[] boundingSpheres = new BoundingSphere[totalLights];

            int lightCullingIndex = 0;
            for(int lightTypeIndex=0; lightTypeIndex < m_Lights.Length; lightTypeIndex++)
            {
                for(int lightIndex=0; lightIndex < m_Lights[lightTypeIndex].Count; lightIndex++)
                {
                    Light2D light = m_Lights[lightTypeIndex][lightIndex];
                    if (light != null)
                    {
                        boundingSpheres[lightCullingIndex] = light.GetBoundingSphere();
                        light.m_LightCullingIndex = lightCullingIndex++;
                    }
                }
            }

            m_CullingGroup.SetBoundingSpheres(boundingSpheres);
        }

        public void InsertLight(Light2D light)
        {
            int index = 0;
            int lightType = (int)m_LightOperation;
            while (index < m_Lights[lightType].Count && m_ShapeLightOrder > m_Lights[lightType][index].m_ShapeLightOrder)
                index++;

            m_Lights[lightType].Insert(index, this);
        }

        public void UpdateLightOperation(LightOperation type)
        {
            if (type != m_PreviousLightOperation)
            {
                m_Lights[(int)m_LightOperation].Remove(this);
                m_LightOperation = type;
                m_PreviousLightOperation = m_LightOperation;
                InsertLight(this);
            }
        }

        public void UpdateLightProjectionType(LightProjectionTypes type)
        {
            if (type != m_PreviousLightProjectionType)
            {
                // Remove the old value
                int index = (int)m_LightOperation;
                if (m_Lights[index].Contains(this))
                    m_Lights[index].Remove(this);

                // Add the new value
                index = (int)m_LightOperation;
                if (!m_Lights[index].Contains(this))
                    m_Lights[index].Add(this);

                m_LightProjectionType = type;
                m_PreviousLightProjectionType = m_LightProjectionType;
            }
        }

        public LightOperation lightOperation
        {
            get { return m_LightOperation; }
            set { UpdateLightOperation(value); }
        }

        public LightProjectionTypes LightProjectionType
        {
            get { return m_LightProjectionType; }
            set { UpdateLightProjectionType(value); }
        }

        [SerializeField]
        Spline m_Spline = new Spline() { isExtensionsSupported = false };
        int m_SplineHash;

        [SerializeField]
        Vector3[] m_ShapePath;
        public Vector3[] shapePath => m_ShapePath;

#if UNITY_EDITOR
        int GetShapePathHash()
        {
            unchecked
            {
                int hashCode = (int)2166136261;

                if (m_ShapePath != null)
                {
                    foreach (var point in m_ShapePath)
                        hashCode = hashCode * 16777619 ^ point.GetHashCode();
                }
                else
                {
                    hashCode = 0;
                }

                return hashCode;
            }
        }

        int m_PrevShapePathHash;
#endif

        private List<Vector2> UpdateFeatheredShapeLightMesh(ContourVertex[] contourPoints, int contourPointCount)
        {
            List<Vector2> feathered = new List<Vector2>();
            for (int i = 0; i < contourPointCount; ++i)
            {
                int h = (i == 0) ? (contourPointCount - 1) : (i - 1);
                int j = (i + 1) % contourPointCount;

                Vector2 pp = new Vector2(contourPoints[h].Position.X, contourPoints[h].Position.Y);
                Vector2 cp = new Vector2(contourPoints[i].Position.X, contourPoints[i].Position.Y);
                Vector2 np = new Vector2(contourPoints[j].Position.X, contourPoints[j].Position.Y);

                Vector2 cpd = cp - pp;
                Vector2 npd = np - cp;
                if (cpd.magnitude < 0.001f || npd.magnitude < 0.001f)
                    continue;

                Vector2 vl = cpd.normalized;
                Vector2 vr = npd.normalized;

                vl = new Vector2(-vl.y, vl.x);
                vr = new Vector2(-vr.y, vr.x);

                Vector2 va = vl.normalized + vr.normalized;
                Vector2 vn = -va.normalized;

                if (va.magnitude > 0 && vn.magnitude > 0)
                {
                    var t = cp + (vn * m_ShapeLightFeathering);
                    feathered.Add(t);
                }
            }
            return feathered;

        }

        internal object InterpCustomVertexData(Vec3 position, object[] data, float[] weights)
        {
            return data[0];
        }


        public void UpdateShapeLightMesh(Color color)
        {
            Color meshInteriorColor = color;
            Color meshFeatherColor = new Color(color.r, color.g, color.b, 0);

            int pointCount = m_ShapePath.Length;
            var inputs = new ContourVertex[pointCount];
            for (int i = 0; i < pointCount; ++i)
                inputs[i] = new ContourVertex() { Position = new Vec3() { X = m_ShapePath[i].x, Y = m_ShapePath[i].y }, Data = meshFeatherColor };
            
            var feathered = UpdateFeatheredShapeLightMesh(inputs, pointCount);
            int featheredPointCount = feathered.Count + pointCount;

            Tess tessI = new Tess();  // Interior
            Tess tessF = new Tess();  // Feathered Edge

            var inputsI = new ContourVertex[pointCount];
            for (int i = 0; i < pointCount - 1; ++i)
            {
                var inputsF = new ContourVertex[4];
                inputsF[0] = new ContourVertex() { Position = new Vec3() { X = m_ShapePath[i].x, Y = m_ShapePath[i].y }, Data = meshInteriorColor };
                inputsF[1] = new ContourVertex() { Position = new Vec3() { X = feathered[i].x, Y = feathered[i].y }, Data = meshFeatherColor };
                inputsF[2] = new ContourVertex() { Position = new Vec3() { X = feathered[i + 1].x, Y = feathered[i + 1].y },  Data = meshFeatherColor };
                inputsF[3] = new ContourVertex() { Position = new Vec3() { X = m_ShapePath[i + 1].x, Y = m_ShapePath[i + 1].y },  Data = meshInteriorColor };
                tessF.AddContour(inputsF, ContourOrientation.Original);

                inputsI[i] = new ContourVertex() { Position = new Vec3() { X = m_ShapePath[i].x, Y = m_ShapePath[i].y }, Data = meshInteriorColor };
            }

            var inputsL = new ContourVertex[4];
            inputsL[0] = new ContourVertex() { Position = new Vec3() { X = m_ShapePath[pointCount - 1].x, Y = m_ShapePath[pointCount - 1].y }, Data = meshInteriorColor };
            inputsL[1] = new ContourVertex() { Position = new Vec3() { X = feathered[pointCount - 1].x, Y = feathered[pointCount - 1].y }, Data = meshFeatherColor };
            inputsL[2] = new ContourVertex() { Position = new Vec3() { X = feathered[0].x, Y = feathered[0].y }, Data = meshFeatherColor };
            inputsL[3] = new ContourVertex() { Position = new Vec3() { X = m_ShapePath[0].x, Y = m_ShapePath[0].y }, Data = meshInteriorColor };
            tessF.AddContour(inputsL, ContourOrientation.Original);

            inputsI[pointCount-1] = new ContourVertex() { Position = new Vec3() { X = m_ShapePath[pointCount - 1].x, Y = m_ShapePath[pointCount - 1].y }, Data = meshInteriorColor };
            tessI.AddContour(inputsI, ContourOrientation.Original);

            tessI.Tessellate(WindingRule.EvenOdd, ElementType.Polygons, 3, InterpCustomVertexData);
            tessF.Tessellate(WindingRule.EvenOdd, ElementType.Polygons, 3, InterpCustomVertexData);

            var indicesI = tessI.Elements.Select(i => i).ToArray();
            var verticesI = tessI.Vertices.Select(v => new Vector3(v.Position.X, v.Position.Y, 0)).ToArray();
            var colorsI = tessI.Vertices.Select(v => new Color(((Color)v.Data).r, ((Color)v.Data).g, ((Color)v.Data).b, ((Color)v.Data).a)).ToArray();

            var indicesF = tessF.Elements.Select(i => i + verticesI.Length).ToArray();
            var verticesF = tessF.Vertices.Select(v => new Vector3(v.Position.X, v.Position.Y, 0)).ToArray();
            var colorsF = tessF.Vertices.Select(v => new Color(((Color)v.Data).r, ((Color)v.Data).g, ((Color)v.Data).b, ((Color)v.Data).a)).ToArray();


            List<Vector3> finalVertices = new List<Vector3>();
            List<int> finalIndices = new List<int>();
            List<Color> finalColors = new List<Color>();
            finalVertices.AddRange(verticesI);
            finalVertices.AddRange(verticesF);
            finalIndices.AddRange(indicesI);
            finalIndices.AddRange(indicesF);
            finalColors.AddRange(colorsI);
            finalColors.AddRange(colorsF);

            var volumeColors = new Vector4[finalColors.Count];
            for (int i = 0; i < volumeColors.Length; i++)
                volumeColors[i] = new Vector4(1, 1, 1, m_LightVolumeOpacity);

            Vector3[] vertices = finalVertices.ToArray();
            m_Mesh.Clear();
            m_Mesh.vertices = vertices;
            m_Mesh.tangents = volumeColors;
            m_Mesh.colors = finalColors.ToArray();
            m_Mesh.SetIndices(finalIndices.ToArray(), MeshTopology.Triangles, 0);

            m_LocalBounds = LightUtility.CalculateBoundingSphere(ref vertices);
        }

        public Material GetVolumeMaterial()
        {
            if (m_LightProjectionType == LightProjectionTypes.Shape)
            {
                if (m_ShapeLightCookieStyle == CookieStyles.Sprite)
                {
                    // This is causing Object.op_inequality fix this
                    if (m_ShapeCookieSpriteVolumeMaterial == null && m_LightCookieSprite && m_LightCookieSprite.texture != null)
                    {
                        Shader shader = Shader.Find("Hidden/Light2d-Sprite-Volumetric");
                        if (shader != null)
                        {
                            m_ShapeCookieSpriteVolumeMaterial = new Material(shader);
                            m_ShapeCookieSpriteVolumeMaterial.SetTexture("_MainTex", m_LightCookieSprite.texture);
                        }
                        else
                            Debug.LogError("Missing shader Light2d-Sprite-Volumetric");
                    }

                    return m_ShapeCookieSpriteVolumeMaterial;
                }
                else
                {
                    // This is causing Object.op_inequality fix this
                    if (m_ShapeVertexColoredVolumeMaterial == null)
                    {
                        Shader shader = Shader.Find("Hidden/Light2d-Shape-Volumetric");
                        if(shader != null)
                            m_ShapeVertexColoredVolumeMaterial = new Material(shader);
                        else
                            Debug.LogError("Missing shader Light2d-Shape-Volumetric");
                    }

                    return m_ShapeVertexColoredVolumeMaterial;
                }
            }
            else if(m_LightProjectionType == LightProjectionTypes.Point)
            {
                if (m_PointLightVolumeMaterial == null)
                {
                    Shader shader = Shader.Find("Hidden/Light2d-Point-Volumetric");
                    if(shader != null )
                    m_PointLightVolumeMaterial = new Material(shader);
                }

                return m_PointLightVolumeMaterial;
            }

            return null;
        }

        public Material GetMaterial()
        {
            if (m_LightProjectionType == LightProjectionTypes.Shape)
            {
                if (m_ShapeLightCookieStyle == CookieStyles.Sprite)
                {
                    // This is causing Object.op_inequality fix this
                    if (m_ShapeCookieSpriteAdditiveMaterial == null && m_LightCookieSprite && m_LightCookieSprite.texture != null)
                    {
                        Shader shader = Shader.Find("Hidden/Light2D-Sprite-Additive");

                        if (shader != null)
                        {
                            m_ShapeCookieSpriteAdditiveMaterial = new Material(shader);
                            m_ShapeCookieSpriteAdditiveMaterial.SetTexture("_MainTex", m_LightCookieSprite.texture);
                        }
                        else
                            Debug.LogError("Missing shader Light2d-Sprite-Additive");
                    }

                    if (m_ShapeCookieSpriteAlphaBlendMaterial == null && m_LightCookieSprite && m_LightCookieSprite.texture != null)
                    {
                        Shader shader = Shader.Find("Hidden/Light2D-Sprite-Superimpose"); ;

                        if (shader != null)
                        {
                            m_ShapeCookieSpriteAlphaBlendMaterial = new Material(shader);
                            m_ShapeCookieSpriteAlphaBlendMaterial.SetTexture("_MainTex", m_LightCookieSprite.texture);
                        }
                        else
                            Debug.LogError("Missing shader Light2d-Sprite-Superimpose");
                    }


                    if (m_ShapeLightOverlapMode == LightOverlapMode.Additive)
                        return m_ShapeCookieSpriteAdditiveMaterial;
                    else
                        return m_ShapeCookieSpriteAlphaBlendMaterial;
                }
                else
                {
                    // This is causing Object.op_inequality fix this
                    if (m_ShapeVertexColoredAdditiveMaterial == null)
                    {
                        Shader shader = Shader.Find("Hidden/Light2D-Shape-Additive"); ;
                        if(shader != null)
                            m_ShapeVertexColoredAdditiveMaterial = new Material(shader);
                        else
                            Debug.LogError("Missing shader Light2d-Shape-Additive");
                    }

                    if (m_ShapeVertexColoredAlphaBlendMaterial == null)
                    {
                        Shader shader = Shader.Find("Hidden/Light2D-Shape-Superimpose"); ;
                        if (shader != null)
                            m_ShapeVertexColoredAlphaBlendMaterial = new Material(shader);
                        else
                            Debug.LogError("Missing shader Light2d-Shape-Superimpose");
                    }

                    if (m_ShapeLightOverlapMode == LightOverlapMode.Additive)
                        return m_ShapeVertexColoredAdditiveMaterial;
                    else
                        return m_ShapeVertexColoredAlphaBlendMaterial;
                }
            }
            if(m_LightProjectionType == LightProjectionTypes.Point)
            {
                if (m_PointLightMaterial == null)
                {
                    Shader shader = Shader.Find("Hidden/Light2D-Point");
                    if (shader != null)
                        m_PointLightMaterial = new Material(shader);
                    else
                        Debug.LogError("Missing shader Light2D-Point");
                }

                return m_PointLightMaterial;
            }

            return null;
        }


        public Mesh GetMesh(bool forceUpdate = false)
        {
            if (m_Mesh == null || forceUpdate)
            {
                if (m_Mesh == null)
                    m_Mesh = new Mesh();

                if (m_LightProjectionType == LightProjectionTypes.Shape)
                {
                    if (m_ShapeLightCookieStyle == CookieStyles.Parametric)
                    {
                        if (m_ShapeLightParametricShape == ParametricShapes.Freeform)
                            UpdateShapeLightMesh(m_LightColor);
                        else
                        {
                            m_LocalBounds = LightUtility.GenerateParametricMesh(ref m_Mesh, 0.5f, m_ShapeLightOffset, m_ShapeLightParametricSides, m_ShapeLightFeathering, m_LightColor, m_LightVolumeOpacity);
                        }
                    }
                    else if (m_ShapeLightCookieStyle == CookieStyles.Sprite)
                    {
                        m_LocalBounds = LightUtility.GenerateSpriteMesh(ref m_Mesh, m_LightCookieSprite, m_LightColor, m_LightVolumeOpacity, 1);
                    }
                }
                else if(m_LightProjectionType == LightProjectionTypes.Point)
                {
                     m_LocalBounds = LightUtility.GenerateParametricMesh(ref m_Mesh, 1.412135f, Vector2.zero, 4, 0, m_LightColor, m_LightVolumeOpacity);
                }
            }

            return m_Mesh;
        }

        public bool IsLitLayer(int layer)
        {
            return m_ApplyToSortingLayers != null ? m_ApplyToSortingLayers.Contains(layer) : false;
        }

        public void UpdateMesh()
        {
            GetMesh(true);
        }

        public void UpdateMaterial()
        {
            m_ShapeCookieSpriteAdditiveMaterial = null;
            m_ShapeCookieSpriteAlphaBlendMaterial = null;
            m_ShapeCookieSpriteVolumeMaterial = null;
            m_PointLightMaterial = null;
            m_PointLightVolumeMaterial = null;
            GetMaterial();
        }

        private void OnDisable()
        {
            bool anyLightLeft = false;

            if (m_Lights != null)
            {
                for (int i = 0; i < m_Lights.Length; i++)
                {
                    if (m_Lights[i].Contains(this))
                        m_Lights[i].Remove(this);

                    if (m_Lights[i].Count > 0)
                        anyLightLeft = true;
                }
            }

            if (!anyLightLeft && m_CullingGroup != null)
            {
                m_CullingGroup.Dispose();
                m_CullingGroup = null;
                RenderPipeline.beginCameraRendering -= SetupCulling;
            }
        }

        public static List<Light2D> GetPointLights()
        {
            return m_Lights[(int)Light2DType.Point];
        }

        public static List<Light2D> GetShapeLights(LightOperation lightOperation)
        {
            return m_Lights[(int)lightOperation];
        }

        void RegisterLight()
        {
            if (m_Lights != null)
            {
                int index = (int)m_LightOperation;
                if (!m_Lights[index].Contains(this))
                    InsertLight(this);
            }
        }

        void Awake()
        {
            if (m_ShapePath == null)
                m_ShapePath = m_Spline.m_ControlPoints.Select(x => x.position).ToArray();

            if (m_ShapePath.Length == 0)
                m_ShapePath = new Vector3[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(0.5f, 0.5f), new Vector3(-0.5f, 0.5f) };

            GetMesh();
        }

        void OnEnable()
        {
            if (m_CullingGroup == null)
            {
                m_CullingGroup = new CullingGroup();
                RenderPipeline.beginCameraRendering += SetupCulling;
            }

            RegisterLight();
        }

        bool CheckForColorChange(Color i, ref Color j)
        {
            bool retVal = i.r != j.r || i.g != j.g || i.b != j.b || i.a != j.a;
            j = i;
            return retVal;
        }

        bool CheckForVector2Change(Vector2 i, ref Vector2 j)
        {
            bool retVal = i.x != j.x || i.y != j.y;
            j = i;
            return retVal;
        }

        bool CheckForSpriteChange(Sprite i, ref Sprite j)
        {
            // If both are null
            bool retVal = false;

            // If one is not null but the other is
            if (i == null ^ j == null)
                retVal = true;

            // if both are not null then do another test
            if (i != null && j != null)
                retVal = i.GetInstanceID() != j.GetInstanceID();

            j = i;
            return retVal;
        }

        bool CheckForChange<T>(T a, ref T b)
        {
            int compareResult = Comparer<T>.Default.Compare(a, b);
            b = a;
            return compareResult != 0;
        }

        private void LateUpdate()
        {
            // Sorting
            if(CheckForChange<int>(m_ShapeLightOrder, ref m_PreviousShapeLightOrder) && this.m_LightProjectionType == LightProjectionTypes.Shape)
            {
                //m_ShapeLightStyle = CookieStyles.Parametric;
                m_Lights[(int)m_LightOperation].Remove(this);
                InsertLight(this);
            }

            // If we changed blending modes then we need to clear our material
            //if(CheckForChange<BlendingModes>(m_ShapeLightOverlapMode, ref m_PreviousShapeLightBlending))
            //{
            //    m_ShapeCookieSpriteMaterial = null;
            //    m_ShapeVertexColoredMaterial = null;
            //}

            // Mesh Rebuilding
            bool rebuildMesh = false;

            rebuildMesh |= CheckForColorChange(m_LightColor, ref m_PreviousLightColor);
            rebuildMesh |= CheckForChange<float>(m_ShapeLightFeathering, ref m_PreviousShapeLightFeathering);
            rebuildMesh |= CheckForVector2Change(m_ShapeLightOffset, ref m_PreviousShapeLightOffset);
            rebuildMesh |= CheckForChange<int>(m_ShapeLightParametricSides, ref m_PreviousShapeLightParametricSides);
            rebuildMesh |= CheckForChange<float>(m_LightVolumeOpacity, ref m_PreviousLightVolumeOpacity);

#if UNITY_EDITOR
            var shapePathHash = GetShapePathHash();
            rebuildMesh |= m_PrevShapePathHash != shapePathHash;
            m_PrevShapePathHash = shapePathHash;
#endif

            if (rebuildMesh)
            {
                UpdateMesh();
            }

            bool rebuildMaterial = false;
            rebuildMaterial |= CheckForSpriteChange(m_LightCookieSprite, ref m_PreviousLightCookieSprite);
            if (rebuildMaterial)
            {
                UpdateMaterial();
                rebuildMaterial = false;
            }

            UpdateLightProjectionType(m_LightProjectionType);
            UpdateLightOperation(m_LightOperation);
        }

        public bool IsLightVisible(Camera camera)
        {
            bool isVisible = (m_CullingGroup == null || m_CullingGroup.IsVisible(m_LightCullingIndex)) && isActiveAndEnabled;

#if UNITY_EDITOR
            isVisible = isVisible && UnityEditor.SceneManagement.StageUtility.IsGameObjectRenderedByCamera(gameObject, camera);
#endif
            return isVisible;
        }

        void OnDrawGizmos()
        {
#if UNITY_EDITOR
            if (Selection.activeGameObject != transform.gameObject)
                Gizmos.DrawIcon(transform.position, "PointLight Gizmo", true);
#endif
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.DrawIcon(transform.position, "PointLight Gizmo", true);
        }
    }
}
