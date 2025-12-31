#version 330

// Input from vertex shader
in vec2 fragTexCoord;
in vec4 fragColor;

// Output color
out vec4 finalColor;

// Uniforms
uniform sampler2D lutTexture;       // RGB = SubtileID
uniform sampler2D valueTexture;     // R = Value (Normalized 0-1)
uniform sampler2D paletteTexture;   // Ramp/Palette (1D gradient)

uniform int subtileCount;           // Total number of subtiles
uniform int valueTextureSize;       // Width of the square value texture
uniform int useRamp;                // 1 = Use Ramp, 0 = Use Direct Color (from value texture RGB)

// Warp Uniforms
uniform float noiseScale;
uniform float warpStrength;
uniform int warpEnabled;

// Constants
const float PI = 3.14159265359;

// ----------------------------------------------------------------------------
// 3D Simplex Noise (Standard GLSL implementation)
// ----------------------------------------------------------------------------
vec3 mod289(vec3 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
vec4 mod289(vec4 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
vec4 permute(vec4 x) { return mod289(((x*34.0)+1.0)*x); }
vec4 taylorInvSqrt(vec4 r) { return 1.79284291400159 - 0.85373472095314 * r; }

float snoise(vec3 v)
{
  const vec2  C = vec2(1.0/6.0, 1.0/3.0) ;
  const vec4  D = vec4(0.0, 0.5, 1.0, 2.0);

  // First corner
  vec3 i  = floor(v + dot(v, C.yyy) );
  vec3 x0 = v - i + dot(i, C.xxx) ;

  // Other corners
  vec3 g = step(x0.yzx, x0.xyz);
  vec3 l = 1.0 - g;
  vec3 i1 = min( g.xyz, l.zxy );
  vec3 i2 = max( g.xyz, l.zxy );

  //   x0 = x0 - 0.0 + 0.0 * C.xxx;
  //   x1 = x0 - i1  + 1.0 * C.xxx;
  //   x2 = x0 - i2  + 2.0 * C.xxx;
  //   x3 = x0 - 1.0 + 3.0 * C.xxx;
  vec3 x1 = x0 - i1 + C.xxx;
  vec3 x2 = x0 - i2 + C.yyy; // 2.0*C.x = 1/3 = C.y
  vec3 x3 = x0 - D.yyy;      // -1.0+3.0*C.x = -0.5 = -D.y

  // Permutations
  i = mod289(i); 
  vec4 p = permute( permute( permute( 
             i.z + vec4(0.0, i1.z, i2.z, 1.0 ))
           + i.y + vec4(0.0, i1.y, i2.y, 1.0 )) 
           + i.x + vec4(0.0, i1.x, i2.x, 1.0 ));

  // Gradients: 7x7 points over a square, mapped onto an octahedron.
  // The ring size 17*17 = 289 is close to a multiple of 49 (49*6 = 294)
  float n_ = 0.142857142857; // 1.0/7.0
  vec3  ns = n_ * D.wyz - D.xzx;

  vec4 j = p - 49.0 * floor(p * ns.z * ns.z);  //  mod(p,7*7)

  vec4 x_ = floor(j * ns.z);
  vec4 y_ = floor(j - 7.0 * x_ );    // mod(j,N)

  vec4 x = x_ *ns.x + ns.yyyy;
  vec4 y = y_ *ns.x + ns.yyyy;
  vec4 h = 1.0 - abs(x) - abs(y);

  vec4 b0 = vec4( x.xy, y.xy );
  vec4 b1 = vec4( x.zw, y.zw );

  //vec4 s0 = vec4(lessThan(b0,0.0))*2.0 - 1.0;
  vec4 s0 = floor(b0)*2.0 + 1.0;
  vec4 s1 = floor(b1)*2.0 + 1.0;
  vec4 sh = -step(h, vec4(0.0));

  vec4 a0 = b0.xzyw + s0.xzyw*sh.xxyy ;
  vec4 a1 = b1.xzyw + s1.xzyw*sh.zzww ;

  vec3 p0 = vec3(a0.xy,h.x);
  vec3 p1 = vec3(a0.zw,h.y);
  vec3 p2 = vec3(a1.xy,h.z);
  vec3 p3 = vec3(a1.zw,h.w);

  //Normalise gradients
  vec4 norm = taylorInvSqrt(vec4(dot(p0,p0), dot(p1,p1), dot(p2, p2), dot(p3,p3)));
  p0 *= norm.x;
  p1 *= norm.y;
  p2 *= norm.z;
  p3 *= norm.w;

  // Mix final noise value
  vec4 m = max(0.5 - vec4(dot(x0,x0), dot(x1,x1), dot(x2,x2), dot(x3,x3)), 0.0);
  m = m * m;
  return 105.0 * dot( m*m, vec4( dot(p0,x0), dot(p1,x1), 
                                dot(p2,x2), dot(p3,x3) ) );
}

// ----------------------------------------------------------------------------
// Coordinate Conversions
// ----------------------------------------------------------------------------

vec3 UVtoSphere(vec2 uv)
{
    float lon = uv.x * 2.0 * PI - PI;
    float lat = PI * 0.5 - uv.y * PI; // y=0 -> North Pole (lat=PI/2)
    
    // lat is -PI/2 to PI/2.
    // phi is 0 (North) to PI (South). 
    // Wait, let's match C# GetPointAtPixel:
    // py_norm = y / Height (0..1)
    // lat = 90 - py_norm*180 -> +90 to -90
    // phi = (90 - lat) -> 0 to 180 (0 to PI)
    // So phi corresponds to uv.y * PI
    
    float phi = uv.y * PI;
    float theta = lon;
    
    float x = sin(phi) * cos(theta);
    float y = cos(phi);
    float z = sin(phi) * sin(theta);
    
    return vec3(x, y, z);
}

vec2 SphereToUV(vec3 p)
{
    // p should be normalized
    float phi = acos(p.y); // 0 to PI
    float theta = atan(p.z, p.x); // -PI to PI
    
    float u = (theta + PI) / (2.0 * PI);
    float v = phi / PI;
    
    return vec2(u, v);
}


// Helper to decode ID from RGB (24-bit)
int DecodeId(vec3 color)
{
    int r = int(color.r * 255.0 + 0.5);
    int g = int(color.g * 255.0 + 0.5);
    int b = int(color.b * 255.0 + 0.5);
    return r + (g << 8) + (b << 16);
}

// Helper to get UV for Value Texture from ID
vec2 GetValueUV(int id)
{
    float x = (float(id % valueTextureSize) + 0.5) / float(valueTextureSize);
    float y = (float(id / valueTextureSize) + 0.5) / float(valueTextureSize);
    return vec2(x, y);
}

// ----------------------------------------------------------------------------
// PIXEL-PERFECT SNAPPING UNIFORM
// ----------------------------------------------------------------------------
uniform vec2 lutSize; // Dimensions of the LUT (e.g., 21600, 10800)

void main()
{
    // Snap the input coordinate to the LUT grid center
    // This ensures that for a single "LUT Pixel" displayed on screen, 
    // we use the exact same UV for noise calculation and lookup.
    vec2 texelSize = 1.0 / lutSize;
    vec2 snappedUV = floor(fragTexCoord * lutSize) / lutSize + (0.5 * texelSize);

    vec2 lookupUV = snappedUV;
    
    // ------------------------------------------------------------------------
    // WARP LOGIC (GPU Optimized)
    // ------------------------------------------------------------------------
    if (warpEnabled == 1 && warpStrength > 0.0001)
    {
        // Use the SNAPPED UV for the position calculation
        // This ensures the noise value is constant across the entire "pixel"
        vec3 pos = UVtoSphere(snappedUV);
        
        // Generate 3D noise vector
        // Using arbitrary offsets to Decorrelate XYZ components
        float nx = snoise(pos * noiseScale);
        float ny = snoise(pos * noiseScale + vec3(13.5, -42.1, 7.8));
        float nz = snoise(pos * noiseScale + vec3(-99.3, 12.4, -5.5));
        
        vec3 noiseVec = vec3(nx, ny, nz);
        
        // Apply Warp
        vec3 warpedPos = pos + noiseVec * warpStrength;
        warpedPos = normalize(warpedPos); // Re-project to surface
        
        // Convert back to UV for texture lookup
        lookupUV = SphereToUV(warpedPos);
    }
    
    // ------------------------------------------------------------------------
    // LUT LOOKUP (Nearest Filtering Assumed)
    // ------------------------------------------------------------------------
    vec3 lutColor = texture(lutTexture, lookupUV).rgb;
    int subtileId = DecodeId(lutColor);

    // Safety check
    if (subtileId >= subtileCount || subtileId < 0)
    {
        finalColor = vec4(1.0, 0.0, 1.0, 1.0); // Error Magenta
        return;
    }

    // ------------------------------------------------------------------------
    // VALUE FETCH
    // ------------------------------------------------------------------------
    vec2 valueUV = GetValueUV(subtileId);
    
    if (useRamp == 1)
    {
        float val = texture(valueTexture, valueUV).r;
        val = clamp(val, 0.0, 1.0);
        vec3 rampColor = texture(paletteTexture, vec2(val, 0.5)).rgb;
        finalColor = vec4(rampColor, 1.0);
    }
    else
    {
        vec3 directColor = texture(valueTexture, valueUV).rgb;
        finalColor = vec4(directColor, 1.0);
    }
}

