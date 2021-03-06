#ifndef GRASS_SIMULATION_ATTRIBUTES_INCLUDED
#define GRASS_SIMULATION_ATTRIBUTES_INCLUDED

struct VSOut 
{
    uint vertexID : VertexID;
    uint instanceID : InstanceID;
    int type : TEXCOORD0;
    float2 uvLocal : TEXCOORD1;
};

struct HSConstOut
{
    float TessFactor[4] : SV_TessFactor;
    float InsideTessFactor[2] : SV_InsideTessFactor;
};

struct HSOut
{
    float3 pos : POS;
    float transitionFactor : TEXCOORD0;
    float4 parameters : TEXCOORD1;
    float3 bladeUp : TEXCOORD2;
    float3 v1 : TEXCOORD3;
    float3 v2 : TEXCOORD4;
    float3 bladeDir : TEXCOORD5;
    float4 grassMapData : TEXCOORD6;
    //float3 bitangent : TEXCOORD7;
    
    #ifdef GRASS_BILLBOARD_CROSSED
        float3 bitangent : TEXCOORD7;
    #endif
    #ifdef GRASS_BILLBOARD_SCREEN
        float3 bitangent : TEXCOORD7;
    #endif
};

struct DSOut
{
    float4 pos : SV_POSITION;
    //float4 color : COLOR0;
    float4 uvwd : TEXCOORD0;
    float3 normal : NORMAL;
    #ifdef GRASS_BILLBOARD_CROSSED
        float3 tangent : TANGENT;
    #endif
    #ifdef GRASS_BILLBOARD_SCREEN
        float3 tangent : TANGENT;
    #endif
};

struct FSIn
{
    float4 pos : SV_POSITION;
    //float4 color : COLOR0;
    float4 uvwd : TEXCOORD0;
    float3 normal : NORMAL;
    #ifdef GRASS_BILLBOARD_CROSSED
        float3 tangent : TANGENT;
    #endif
    #ifdef GRASS_BILLBOARD_SCREEN
        float3 tangent : TANGENT;
    #endif
};

#endif //GRASS_SIMULATION_ATTRIBUTES_INCLUDED