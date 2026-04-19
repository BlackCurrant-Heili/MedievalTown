#ifndef MATH_HLSL
#define MATH_HLSL

#define HALF_PI 1.570796327
#define PI 3.141592653
#define TWO_PI 6.283185307

float rand(float3 co) {
    return frac(sin(dot(co, float3(12.9898, 78.233, 53.539))) * 43758.5453);
}

float2 rand2(float3 co) {
    return float2(rand(co.xyz), rand(co.yzx));
}

float3 rand3(float3 co) {
    return float3(rand(co.xyz), rand(co.yzx), rand(co.zxy));
}

float3x3 _AngleAxis3x3(float angle, float3 axis) {
    float c, s;
    sincos(angle, s, c);

    float t = 1 - c;
    float x = axis.x;
    float y = axis.y;
    float z = axis.z;

    return float3x3(
        t * x * x + c, t * x * y - s * z, t * x * z + s * y,
        t * x * y + s * z, t * y * y + c, t * y * z - s * x,
        t * x * z - s * y, t * y * z + s * x, t * z * z + c);
}

float3x3 AngleAxis3x3(float angle, float3 axis) {
    angle = clamp(angle, -HALF_PI, HALF_PI);
    return _AngleAxis3x3(angle, axis);
}

#endif