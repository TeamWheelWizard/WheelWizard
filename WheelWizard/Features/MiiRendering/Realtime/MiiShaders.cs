namespace WheelWizard.MiiRendering.Realtime;

/// <summary>
/// GLSL ports of WheelWizard's CPU Mii shading (NativeMiiRenderer.EvaluateModulateColor) so the
/// realtime view looks like WheelWizard's rendered images. Written for both GLSL 330 core and GLSL ES 300.
/// </summary>
internal static class MiiShaders
{
    public static string Header(bool isEs) =>
        isEs ? "#version 300 es\nprecision highp float;\nprecision highp int;\n" : "#version 330 core\n";

    // Shared lighting, identical to the CPU path.
    private const string Lighting = """
        uniform vec3 uLightAmb;
        uniform vec3 uLightDiff;
        uniform vec3 uLightSpec;
        uniform vec3 uLightDir;
        uniform vec3 uMatAmb;
        uniform vec3 uMatDiff;
        uniform vec3 uMatSpec;
        uniform vec3 uMatRim;
        uniform float uSpecPow;
        uniform int uSpecMode;
        uniform float uProfile[7]; // ambientScale, dirInfluence, diffuseScale, diffuseFloor, specularScale, rimScale, rimPower

        vec3 shade(vec3 baseRgb, vec3 viewPos, vec3 normal, vec3 tangent, vec4 param)
        {
            vec3 n = length(normal) > 1e-5 ? normalize(normal) : vec3(0.0, 0.0, 1.0);
            vec3 eye = length(viewPos) > 1e-5 ? normalize(-viewPos) : vec3(0.0, 0.0, 1.0);
            vec3 l = uLightDir;
            vec3 ambient = uLightAmb * uMatAmb * uProfile[0];
            float dirDiffuse = max(dot(l, n), uProfile[3]);
            float diffuseFactor = 1.0 + (dirDiffuse - 1.0) * uProfile[1];
            vec3 diffuse = uLightDiff * uMatDiff * (diffuseFactor * uProfile[2]);
            float blinn = pow(max(dot(reflect(-l, n), eye), 0.0), uSpecPow);
            float strength = param.y;
            float reflection;
            if (uSpecMode == 0) {
                strength = 1.0;
                reflection = blinn;
            } else {
                vec3 t = length(tangent) > 1e-5 ? normalize(tangent) : vec3(1.0, 0.0, 0.0);
                float dotLt = dot(l, t);
                float dotVt = dot(eye, t);
                float dotLn = sqrt(max(0.0, 1.0 - dotLt * dotLt));
                float dotVr = dotLn * sqrt(max(0.0, 1.0 - dotVt * dotVt)) - dotLt * dotVt;
                float aniso = pow(max(0.0, dotVr), uSpecPow);
                reflection = aniso + (blinn - aniso) * param.x;
            }
            vec3 specular = uLightSpec * uMatSpec * reflection * strength * uProfile[4];
            float rimFactor = pow(max(0.0, param.w * (1.0 - abs(n.z))), uProfile[6]);
            vec3 rim = uMatRim * (rimFactor * uProfile[5]);
            return clamp((ambient + diffuse) * baseRgb + specular + rim, 0.0, 1.0);
        }
        """;

    public const string HeadVertex = """
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in vec3 aTangent;
        layout(location = 3) in vec2 aUv;
        layout(location = 4) in vec4 aParam;
        uniform mat4 uModel;
        uniform mat4 uView;
        uniform mat4 uProj;
        out vec3 vViewPos;
        out vec3 vNormal;
        out vec3 vTangent;
        out vec2 vUv;
        out vec4 vParam;
        void main() {
            mat4 modelView = uView * uModel;
            vec4 viewPos = modelView * vec4(aPos, 1.0);
            vViewPos = viewPos.xyz;
            vNormal = mat3(modelView) * aNormal;
            vTangent = mat3(modelView) * aTangent;
            vUv = aUv;
            vParam = aParam;
            gl_Position = uProj * viewPos;
        }
        """;

    public const string HeadFragment =
        Lighting
        + """

            in vec3 vViewPos;
            in vec3 vNormal;
            in vec3 vTangent;
            in vec2 vUv;
            in vec4 vParam;
            uniform int uMode;
            uniform vec4 uColR;
            uniform vec4 uColG;
            uniform vec4 uColB;
            uniform sampler2D uTex;
            uniform int uHasTex;
            uniform int uHasTangent;
            uniform vec4 uTint; // rgb = color to mix towards, a = mix amount
            uniform float uAlpha;
            uniform vec2 uUvOffset;
            out vec4 fragColor;
            void main() {
                vec4 t = uHasTex != 0 ? texture(uTex, vUv + uUvOffset) : vec4(1.0);
                vec4 base;
                if (uMode == 0) base = vec4(uColR.rgb, 1.0);
                else if (uMode == 1) base = t;
                else if (uMode == 2) base = vec4(t.r * uColR.rgb + t.g * uColG.rgb + t.b * uColB.rgb, t.a);
                else if (uMode == 3) base = vec4(uColR.rgb, t.r);
                else if (uMode == 4) base = vec4(t.g * uColR.rgb, t.r);
                else if (uMode == 5) base = vec4(t.r * uColR.rgb, 1.0);
                else base = vec4(1.0);
                if (uMode != 0 && base.a <= 0.0) discard;
                vec3 lit = shade(base.rgb, vViewPos, vNormal, uHasTangent != 0 ? vTangent : vec3(0.0), vParam);
                lit = mix(lit, uTint.rgb, uTint.a);
                fragColor = vec4(lit, clamp(base.a, 0.0, 1.0) * uAlpha);
            }
            """;

    public const string BodyVertex = """
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in vec4 aJoints;
        layout(location = 3) in vec4 aWeights;
        uniform mat4 uBones[16];
        uniform mat4 uView;
        uniform mat4 uProj;
        uniform int uHighlightMask;
        uniform int uHoverMask;
        out vec3 vViewPos;
        out vec3 vNormal;
        out float vHighlight;
        out float vHover;
        void main() {
            mat4 skin = uBones[int(aJoints.x)] * aWeights.x + uBones[int(aJoints.y)] * aWeights.y
                      + uBones[int(aJoints.z)] * aWeights.z + uBones[int(aJoints.w)] * aWeights.w;
            vec4 viewPos = uView * skin * vec4(aPos, 1.0);
            vViewPos = viewPos.xyz;
            vNormal = mat3(uView) * mat3(skin) * aNormal;
            int dominant = int(aJoints.x);
            vHighlight = ((uHighlightMask >> dominant) & 1) != 0 ? 1.0 : 0.0;
            vHover = ((uHoverMask >> dominant) & 1) != 0 ? 1.0 : 0.0;
            gl_Position = uProj * viewPos;
        }
        """;

    public const string BodyFragment =
        Lighting
        + """

            in vec3 vViewPos;
            in vec3 vNormal;
            in float vHighlight;
            in float vHover;
            uniform vec4 uColor;
            uniform vec4 uTint;
            uniform float uAlpha;
            out vec4 fragColor;
            void main() {
                vec3 lit = shade(uColor.rgb, vViewPos, vNormal, vec3(0.0), vec4(1.0, 1.0, 0.0, 1.0));
                lit = mix(lit, lit * vec3(1.25, 1.05, 0.55) + vec3(0.18, 0.1, 0.0), vHighlight * 0.6);
                lit = mix(lit, vec3(1.0), vHover * (1.0 - vHighlight) * 0.25);
                lit = mix(lit, uTint.rgb, uTint.a);
                fragColor = vec4(lit, uAlpha);
            }
            """;

    public const string LineVertex = """
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec4 aColor;
        uniform mat4 uView;
        uniform mat4 uProj;
        out vec4 vColor;
        out float vDist;
        void main() {
            vec4 viewPos = uView * vec4(aPos, 1.0);
            vColor = aColor;
            vDist = length(aPos.xz);
            gl_Position = uProj * viewPos;
        }
        """;

    public const string LineFragment = """
        in vec4 vColor;
        in float vDist;
        out vec4 fragColor;
        void main() {
            float fade = 1.0 - smoothstep(180.0, 320.0, vDist);
            fragColor = vec4(vColor.rgb, vColor.a * fade);
        }
        """;
}
