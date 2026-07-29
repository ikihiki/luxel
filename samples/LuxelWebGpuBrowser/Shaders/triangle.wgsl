struct Root { buffer_index: u32, texture_index: u32, sampler_index: u32, pad0: u32 }
@group(0) @binding(0) var<storage, read> arena: array<u32>;
@group(0) @binding(1) var<uniform> root: Root;
@group(1) @binding(0) var sampled_texture_0: texture_2d<f32>;
@group(1) @binding(1) var sampled_texture_1: texture_2d<f32>;
@group(1) @binding(2) var sampled_texture_2: texture_2d<f32>;
@group(1) @binding(3) var sampled_texture_3: texture_2d<f32>;
@group(1) @binding(4) var sampled_texture_4: texture_2d<f32>;
@group(1) @binding(5) var sampled_texture_5: texture_2d<f32>;
@group(1) @binding(6) var sampled_texture_6: texture_2d<f32>;
@group(1) @binding(7) var sampled_texture_7: texture_2d<f32>;
@group(1) @binding(8) var sampled_texture_8: texture_2d<f32>;
@group(1) @binding(9) var sampled_texture_9: texture_2d<f32>;
@group(1) @binding(10) var sampled_texture_10: texture_2d<f32>;
@group(1) @binding(11) var sampled_texture_11: texture_2d<f32>;
@group(1) @binding(12) var sampled_texture_12: texture_2d<f32>;
@group(1) @binding(13) var sampled_texture_13: texture_2d<f32>;
@group(1) @binding(14) var sampled_texture_14: texture_2d<f32>;
@group(1) @binding(15) var sampled_texture_15: texture_2d<f32>;
@group(1) @binding(16) var sampled_sampler_0: sampler;
@group(1) @binding(17) var sampled_sampler_1: sampler;
@group(1) @binding(18) var sampled_sampler_2: sampler;
@group(1) @binding(19) var sampled_sampler_3: sampler;
@group(1) @binding(20) var sampled_sampler_4: sampler;
@group(1) @binding(21) var sampled_sampler_5: sampler;
@group(1) @binding(22) var sampled_sampler_6: sampler;
@group(1) @binding(23) var sampled_sampler_7: sampler;
@group(1) @binding(24) var sampled_sampler_8: sampler;
@group(1) @binding(25) var sampled_sampler_9: sampler;
@group(1) @binding(26) var sampled_sampler_10: sampler;
@group(1) @binding(27) var sampled_sampler_11: sampler;
@group(1) @binding(28) var sampled_sampler_12: sampler;
@group(1) @binding(29) var sampled_sampler_13: sampler;
@group(1) @binding(30) var sampled_sampler_14: sampler;
@group(1) @binding(31) var sampled_sampler_15: sampler;
fn sample_selected(t: u32, smp: u32) -> vec4<f32> {
  switch t {
    case 0u: { switch smp {
      case 0u: { return textureSample(sampled_texture_0, sampled_sampler_0, vec2<f32>(0.25, 0.25)); }
      case 1u: { return textureSample(sampled_texture_0, sampled_sampler_1, vec2<f32>(0.25, 0.25)); }
      case 2u: { return textureSample(sampled_texture_0, sampled_sampler_2, vec2<f32>(0.25, 0.25)); }
      case 3u: { return textureSample(sampled_texture_0, sampled_sampler_3, vec2<f32>(0.25, 0.25)); }
      case 4u: { return textureSample(sampled_texture_0, sampled_sampler_4, vec2<f32>(0.25, 0.25)); }
      case 5u: { return textureSample(sampled_texture_0, sampled_sampler_5, vec2<f32>(0.25, 0.25)); }
      case 6u: { return textureSample(sampled_texture_0, sampled_sampler_6, vec2<f32>(0.25, 0.25)); }
      case 7u: { return textureSample(sampled_texture_0, sampled_sampler_7, vec2<f32>(0.25, 0.25)); }
      case 8u: { return textureSample(sampled_texture_0, sampled_sampler_8, vec2<f32>(0.25, 0.25)); }
      case 9u: { return textureSample(sampled_texture_0, sampled_sampler_9, vec2<f32>(0.25, 0.25)); }
      case 10u: { return textureSample(sampled_texture_0, sampled_sampler_10, vec2<f32>(0.25, 0.25)); }
      case 11u: { return textureSample(sampled_texture_0, sampled_sampler_11, vec2<f32>(0.25, 0.25)); }
      case 12u: { return textureSample(sampled_texture_0, sampled_sampler_12, vec2<f32>(0.25, 0.25)); }
      case 13u: { return textureSample(sampled_texture_0, sampled_sampler_13, vec2<f32>(0.25, 0.25)); }
      case 14u: { return textureSample(sampled_texture_0, sampled_sampler_14, vec2<f32>(0.25, 0.25)); }
      case 15u: { return textureSample(sampled_texture_0, sampled_sampler_15, vec2<f32>(0.25, 0.25)); }
      default: { return vec4<f32>(1.0, 0.0, 1.0, 1.0); }
    } }
    case 1u: { switch smp {
      case 0u: { return textureSample(sampled_texture_1, sampled_sampler_0, vec2<f32>(0.25, 0.25)); }
      case 1u: { return textureSample(sampled_texture_1, sampled_sampler_1, vec2<f32>(0.25, 0.25)); }
      case 2u: { return textureSample(sampled_texture_1, sampled_sampler_2, vec2<f32>(0.25, 0.25)); }
      case 3u: { return textureSample(sampled_texture_1, sampled_sampler_3, vec2<f32>(0.25, 0.25)); }
      case 4u: { return textureSample(sampled_texture_1, sampled_sampler_4, vec2<f32>(0.25, 0.25)); }
      case 5u: { return textureSample(sampled_texture_1, sampled_sampler_5, vec2<f32>(0.25, 0.25)); }
      case 6u: { return textureSample(sampled_texture_1, sampled_sampler_6, vec2<f32>(0.25, 0.25)); }
      case 7u: { return textureSample(sampled_texture_1, sampled_sampler_7, vec2<f32>(0.25, 0.25)); }
      case 8u: { return textureSample(sampled_texture_1, sampled_sampler_8, vec2<f32>(0.25, 0.25)); }
      case 9u: { return textureSample(sampled_texture_1, sampled_sampler_9, vec2<f32>(0.25, 0.25)); }
      case 10u: { return textureSample(sampled_texture_1, sampled_sampler_10, vec2<f32>(0.25, 0.25)); }
      case 11u: { return textureSample(sampled_texture_1, sampled_sampler_11, vec2<f32>(0.25, 0.25)); }
      case 12u: { return textureSample(sampled_texture_1, sampled_sampler_12, vec2<f32>(0.25, 0.25)); }
      case 13u: { return textureSample(sampled_texture_1, sampled_sampler_13, vec2<f32>(0.25, 0.25)); }
      case 14u: { return textureSample(sampled_texture_1, sampled_sampler_14, vec2<f32>(0.25, 0.25)); }
      case 15u: { return textureSample(sampled_texture_1, sampled_sampler_15, vec2<f32>(0.25, 0.25)); }
      default: { return vec4<f32>(1.0, 0.0, 1.0, 1.0); }
    } }
    case 2u: { switch smp {
      case 0u: { return textureSample(sampled_texture_2, sampled_sampler_0, vec2<f32>(0.25, 0.25)); }
      case 1u: { return textureSample(sampled_texture_2, sampled_sampler_1, vec2<f32>(0.25, 0.25)); }
      case 2u: { return textureSample(sampled_texture_2, sampled_sampler_2, vec2<f32>(0.25, 0.25)); }
      case 3u: { return textureSample(sampled_texture_2, sampled_sampler_3, vec2<f32>(0.25, 0.25)); }
      case 4u: { return textureSample(sampled_texture_2, sampled_sampler_4, vec2<f32>(0.25, 0.25)); }
      case 5u: { return textureSample(sampled_texture_2, sampled_sampler_5, vec2<f32>(0.25, 0.25)); }
      case 6u: { return textureSample(sampled_texture_2, sampled_sampler_6, vec2<f32>(0.25, 0.25)); }
      case 7u: { return textureSample(sampled_texture_2, sampled_sampler_7, vec2<f32>(0.25, 0.25)); }
      case 8u: { return textureSample(sampled_texture_2, sampled_sampler_8, vec2<f32>(0.25, 0.25)); }
      case 9u: { return textureSample(sampled_texture_2, sampled_sampler_9, vec2<f32>(0.25, 0.25)); }
      case 10u: { return textureSample(sampled_texture_2, sampled_sampler_10, vec2<f32>(0.25, 0.25)); }
      case 11u: { return textureSample(sampled_texture_2, sampled_sampler_11, vec2<f32>(0.25, 0.25)); }
      case 12u: { return textureSample(sampled_texture_2, sampled_sampler_12, vec2<f32>(0.25, 0.25)); }
      case 13u: { return textureSample(sampled_texture_2, sampled_sampler_13, vec2<f32>(0.25, 0.25)); }
      case 14u: { return textureSample(sampled_texture_2, sampled_sampler_14, vec2<f32>(0.25, 0.25)); }
      case 15u: { return textureSample(sampled_texture_2, sampled_sampler_15, vec2<f32>(0.25, 0.25)); }
      default: { return vec4<f32>(1.0, 0.0, 1.0, 1.0); }
    } }
    case 3u: { switch smp {
      case 0u: { return textureSample(sampled_texture_3, sampled_sampler_0, vec2<f32>(0.25, 0.25)); }
      case 1u: { return textureSample(sampled_texture_3, sampled_sampler_1, vec2<f32>(0.25, 0.25)); }
      case 2u: { return textureSample(sampled_texture_3, sampled_sampler_2, vec2<f32>(0.25, 0.25)); }
      case 3u: { return textureSample(sampled_texture_3, sampled_sampler_3, vec2<f32>(0.25, 0.25)); }
      case 4u: { return textureSample(sampled_texture_3, sampled_sampler_4, vec2<f32>(0.25, 0.25)); }
      case 5u: { return textureSample(sampled_texture_3, sampled_sampler_5, vec2<f32>(0.25, 0.25)); }
      case 6u: { return textureSample(sampled_texture_3, sampled_sampler_6, vec2<f32>(0.25, 0.25)); }
      case 7u: { return textureSample(sampled_texture_3, sampled_sampler_7, vec2<f32>(0.25, 0.25)); }
      case 8u: { return textureSample(sampled_texture_3, sampled_sampler_8, vec2<f32>(0.25, 0.25)); }
      case 9u: { return textureSample(sampled_texture_3, sampled_sampler_9, vec2<f32>(0.25, 0.25)); }
      case 10u: { return textureSample(sampled_texture_3, sampled_sampler_10, vec2<f32>(0.25, 0.25)); }
      case 11u: { return textureSample(sampled_texture_3, sampled_sampler_11, vec2<f32>(0.25, 0.25)); }
      case 12u: { return textureSample(sampled_texture_3, sampled_sampler_12, vec2<f32>(0.25, 0.25)); }
      case 13u: { return textureSample(sampled_texture_3, sampled_sampler_13, vec2<f32>(0.25, 0.25)); }
      case 14u: { return textureSample(sampled_texture_3, sampled_sampler_14, vec2<f32>(0.25, 0.25)); }
      case 15u: { return textureSample(sampled_texture_3, sampled_sampler_15, vec2<f32>(0.25, 0.25)); }
      default: { return vec4<f32>(1.0, 0.0, 1.0, 1.0); }
    } }
    case 4u: { switch smp {
      case 0u: { return textureSample(sampled_texture_4, sampled_sampler_0, vec2<f32>(0.25, 0.25)); }
      case 1u: { return textureSample(sampled_texture_4, sampled_sampler_1, vec2<f32>(0.25, 0.25)); }
      case 2u: { return textureSample(sampled_texture_4, sampled_sampler_2, vec2<f32>(0.25, 0.25)); }
      case 3u: { return textureSample(sampled_texture_4, sampled_sampler_3, vec2<f32>(0.25, 0.25)); }
      case 4u: { return textureSample(sampled_texture_4, sampled_sampler_4, vec2<f32>(0.25, 0.25)); }
      case 5u: { return textureSample(sampled_texture_4, sampled_sampler_5, vec2<f32>(0.25, 0.25)); }
      case 6u: { return textureSample(sampled_texture_4, sampled_sampler_6, vec2<f32>(0.25, 0.25)); }
      case 7u: { return textureSample(sampled_texture_4, sampled_sampler_7, vec2<f32>(0.25, 0.25)); }
      case 8u: { return textureSample(sampled_texture_4, sampled_sampler_8, vec2<f32>(0.25, 0.25)); }
      case 9u: { return textureSample(sampled_texture_4, sampled_sampler_9, vec2<f32>(0.25, 0.25)); }
      case 10u: { return textureSample(sampled_texture_4, sampled_sampler_10, vec2<f32>(0.25, 0.25)); }
      case 11u: { return textureSample(sampled_texture_4, sampled_sampler_11, vec2<f32>(0.25, 0.25)); }
      case 12u: { return textureSample(sampled_texture_4, sampled_sampler_12, vec2<f32>(0.25, 0.25)); }
      case 13u: { return textureSample(sampled_texture_4, sampled_sampler_13, vec2<f32>(0.25, 0.25)); }
      case 14u: { return textureSample(sampled_texture_4, sampled_sampler_14, vec2<f32>(0.25, 0.25)); }
      case 15u: { return textureSample(sampled_texture_4, sampled_sampler_15, vec2<f32>(0.25, 0.25)); }
      default: { return vec4<f32>(1.0, 0.0, 1.0, 1.0); }
    } }
    case 5u: { switch smp {
      case 0u: { return textureSample(sampled_texture_5, sampled_sampler_0, vec2<f32>(0.25, 0.25)); }
      case 1u: { return textureSample(sampled_texture_5, sampled_sampler_1, vec2<f32>(0.25, 0.25)); }
      case 2u: { return textureSample(sampled_texture_5, sampled_sampler_2, vec2<f32>(0.25, 0.25)); }
      case 3u: { return textureSample(sampled_texture_5, sampled_sampler_3, vec2<f32>(0.25, 0.25)); }
      case 4u: { return textureSample(sampled_texture_5, sampled_sampler_4, vec2<f32>(0.25, 0.25)); }
      case 5u: { return textureSample(sampled_texture_5, sampled_sampler_5, vec2<f32>(0.25, 0.25)); }
      case 6u: { return textureSample(sampled_texture_5, sampled_sampler_6, vec2<f32>(0.25, 0.25)); }
      case 7u: { return textureSample(sampled_texture_5, sampled_sampler_7, vec2<f32>(0.25, 0.25)); }
      case 8u: { return textureSample(sampled_texture_5, sampled_sampler_8, vec2<f32>(0.25, 0.25)); }
      case 9u: { return textureSample(sampled_texture_5, sampled_sampler_9, vec2<f32>(0.25, 0.25)); }
      case 10u: { return textureSample(sampled_texture_5, sampled_sampler_10, vec2<f32>(0.25, 0.25)); }
      case 11u: { return textureSample(sampled_texture_5, sampled_sampler_11, vec2<f32>(0.25, 0.25)); }
      case 12u: { return textureSample(sampled_texture_5, sampled_sampler_12, vec2<f32>(0.25, 0.25)); }
      case 13u: { return textureSample(sampled_texture_5, sampled_sampler_13, vec2<f32>(0.25, 0.25)); }
      case 14u: { return textureSample(sampled_texture_5, sampled_sampler_14, vec2<f32>(0.25, 0.25)); }
      case 15u: { return textureSample(sampled_texture_5, sampled_sampler_15, vec2<f32>(0.25, 0.25)); }
      default: { return vec4<f32>(1.0, 0.0, 1.0, 1.0); }
    } }
    case 6u: { switch smp {
      case 0u: { return textureSample(sampled_texture_6, sampled_sampler_0, vec2<f32>(0.25, 0.25)); }
      case 1u: { return textureSample(sampled_texture_6, sampled_sampler_1, vec2<f32>(0.25, 0.25)); }
      case 2u: { return textureSample(sampled_texture_6, sampled_sampler_2, vec2<f32>(0.25, 0.25)); }
      case 3u: { return textureSample(sampled_texture_6, sampled_sampler_3, vec2<f32>(0.25, 0.25)); }
      case 4u: { return textureSample(sampled_texture_6, sampled_sampler_4, vec2<f32>(0.25, 0.25)); }
      case 5u: { return textureSample(sampled_texture_6, sampled_sampler_5, vec2<f32>(0.25, 0.25)); }
      case 6u: { return textureSample(sampled_texture_6, sampled_sampler_6, vec2<f32>(0.25, 0.25)); }
      case 7u: { return textureSample(sampled_texture_6, sampled_sampler_7, vec2<f32>(0.25, 0.25)); }
      case 8u: { return textureSample(sampled_texture_6, sampled_sampler_8, vec2<f32>(0.25, 0.25)); }
      case 9u: { return textureSample(sampled_texture_6, sampled_sampler_9, vec2<f32>(0.25, 0.25)); }
      case 10u: { return textureSample(sampled_texture_6, sampled_sampler_10, vec2<f32>(0.25, 0.25)); }
      case 11u: { return textureSample(sampled_texture_6, sampled_sampler_11, vec2<f32>(0.25, 0.25)); }
      case 12u: { return textureSample(sampled_texture_6, sampled_sampler_12, vec2<f32>(0.25, 0.25)); }
      case 13u: { return textureSample(sampled_texture_6, sampled_sampler_13, vec2<f32>(0.25, 0.25)); }
      case 14u: { return textureSample(sampled_texture_6, sampled_sampler_14, vec2<f32>(0.25, 0.25)); }
      case 15u: { return textureSample(sampled_texture_6, sampled_sampler_15, vec2<f32>(0.25, 0.25)); }
      default: { return vec4<f32>(1.0, 0.0, 1.0, 1.0); }
    } }
    case 7u: { switch smp {
      case 0u: { return textureSample(sampled_texture_7, sampled_sampler_0, vec2<f32>(0.25, 0.25)); }
      case 1u: { return textureSample(sampled_texture_7, sampled_sampler_1, vec2<f32>(0.25, 0.25)); }
      case 2u: { return textureSample(sampled_texture_7, sampled_sampler_2, vec2<f32>(0.25, 0.25)); }
      case 3u: { return textureSample(sampled_texture_7, sampled_sampler_3, vec2<f32>(0.25, 0.25)); }
      case 4u: { return textureSample(sampled_texture_7, sampled_sampler_4, vec2<f32>(0.25, 0.25)); }
      case 5u: { return textureSample(sampled_texture_7, sampled_sampler_5, vec2<f32>(0.25, 0.25)); }
      case 6u: { return textureSample(sampled_texture_7, sampled_sampler_6, vec2<f32>(0.25, 0.25)); }
      case 7u: { return textureSample(sampled_texture_7, sampled_sampler_7, vec2<f32>(0.25, 0.25)); }
      case 8u: { return textureSample(sampled_texture_7, sampled_sampler_8, vec2<f32>(0.25, 0.25)); }
      case 9u: { return textureSample(sampled_texture_7, sampled_sampler_9, vec2<f32>(0.25, 0.25)); }
      case 10u: { return textureSample(sampled_texture_7, sampled_sampler_10, vec2<f32>(0.25, 0.25)); }
      case 11u: { return textureSample(sampled_texture_7, sampled_sampler_11, vec2<f32>(0.25, 0.25)); }
      case 12u: { return textureSample(sampled_texture_7, sampled_sampler_12, vec2<f32>(0.25, 0.25)); }
      case 13u: { return textureSample(sampled_texture_7, sampled_sampler_13, vec2<f32>(0.25, 0.25)); }
      case 14u: { return textureSample(sampled_texture_7, sampled_sampler_14, vec2<f32>(0.25, 0.25)); }
      case 15u: { return textureSample(sampled_texture_7, sampled_sampler_15, vec2<f32>(0.25, 0.25)); }
      default: { return vec4<f32>(1.0, 0.0, 1.0, 1.0); }
    } }
    case 8u: { switch smp {
      case 0u: { return textureSample(sampled_texture_8, sampled_sampler_0, vec2<f32>(0.25, 0.25)); }
      case 1u: { return textureSample(sampled_texture_8, sampled_sampler_1, vec2<f32>(0.25, 0.25)); }
      case 2u: { return textureSample(sampled_texture_8, sampled_sampler_2, vec2<f32>(0.25, 0.25)); }
      case 3u: { return textureSample(sampled_texture_8, sampled_sampler_3, vec2<f32>(0.25, 0.25)); }
      case 4u: { return textureSample(sampled_texture_8, sampled_sampler_4, vec2<f32>(0.25, 0.25)); }
      case 5u: { return textureSample(sampled_texture_8, sampled_sampler_5, vec2<f32>(0.25, 0.25)); }
      case 6u: { return textureSample(sampled_texture_8, sampled_sampler_6, vec2<f32>(0.25, 0.25)); }
      case 7u: { return textureSample(sampled_texture_8, sampled_sampler_7, vec2<f32>(0.25, 0.25)); }
      case 8u: { return textureSample(sampled_texture_8, sampled_sampler_8, vec2<f32>(0.25, 0.25)); }
      case 9u: { return textureSample(sampled_texture_8, sampled_sampler_9, vec2<f32>(0.25, 0.25)); }
      case 10u: { return textureSample(sampled_texture_8, sampled_sampler_10, vec2<f32>(0.25, 0.25)); }
      case 11u: { return textureSample(sampled_texture_8, sampled_sampler_11, vec2<f32>(0.25, 0.25)); }
      case 12u: { return textureSample(sampled_texture_8, sampled_sampler_12, vec2<f32>(0.25, 0.25)); }
      case 13u: { return textureSample(sampled_texture_8, sampled_sampler_13, vec2<f32>(0.25, 0.25)); }
      case 14u: { return textureSample(sampled_texture_8, sampled_sampler_14, vec2<f32>(0.25, 0.25)); }
      case 15u: { return textureSample(sampled_texture_8, sampled_sampler_15, vec2<f32>(0.25, 0.25)); }
      default: { return vec4<f32>(1.0, 0.0, 1.0, 1.0); }
    } }
    case 9u: { switch smp {
      case 0u: { return textureSample(sampled_texture_9, sampled_sampler_0, vec2<f32>(0.25, 0.25)); }
      case 1u: { return textureSample(sampled_texture_9, sampled_sampler_1, vec2<f32>(0.25, 0.25)); }
      case 2u: { return textureSample(sampled_texture_9, sampled_sampler_2, vec2<f32>(0.25, 0.25)); }
      case 3u: { return textureSample(sampled_texture_9, sampled_sampler_3, vec2<f32>(0.25, 0.25)); }
      case 4u: { return textureSample(sampled_texture_9, sampled_sampler_4, vec2<f32>(0.25, 0.25)); }
      case 5u: { return textureSample(sampled_texture_9, sampled_sampler_5, vec2<f32>(0.25, 0.25)); }
      case 6u: { return textureSample(sampled_texture_9, sampled_sampler_6, vec2<f32>(0.25, 0.25)); }
      case 7u: { return textureSample(sampled_texture_9, sampled_sampler_7, vec2<f32>(0.25, 0.25)); }
      case 8u: { return textureSample(sampled_texture_9, sampled_sampler_8, vec2<f32>(0.25, 0.25)); }
      case 9u: { return textureSample(sampled_texture_9, sampled_sampler_9, vec2<f32>(0.25, 0.25)); }
      case 10u: { return textureSample(sampled_texture_9, sampled_sampler_10, vec2<f32>(0.25, 0.25)); }
      case 11u: { return textureSample(sampled_texture_9, sampled_sampler_11, vec2<f32>(0.25, 0.25)); }
      case 12u: { return textureSample(sampled_texture_9, sampled_sampler_12, vec2<f32>(0.25, 0.25)); }
      case 13u: { return textureSample(sampled_texture_9, sampled_sampler_13, vec2<f32>(0.25, 0.25)); }
      case 14u: { return textureSample(sampled_texture_9, sampled_sampler_14, vec2<f32>(0.25, 0.25)); }
      case 15u: { return textureSample(sampled_texture_9, sampled_sampler_15, vec2<f32>(0.25, 0.25)); }
      default: { return vec4<f32>(1.0, 0.0, 1.0, 1.0); }
    } }
    case 10u: { switch smp {
      case 0u: { return textureSample(sampled_texture_10, sampled_sampler_0, vec2<f32>(0.25, 0.25)); }
      case 1u: { return textureSample(sampled_texture_10, sampled_sampler_1, vec2<f32>(0.25, 0.25)); }
      case 2u: { return textureSample(sampled_texture_10, sampled_sampler_2, vec2<f32>(0.25, 0.25)); }
      case 3u: { return textureSample(sampled_texture_10, sampled_sampler_3, vec2<f32>(0.25, 0.25)); }
      case 4u: { return textureSample(sampled_texture_10, sampled_sampler_4, vec2<f32>(0.25, 0.25)); }
      case 5u: { return textureSample(sampled_texture_10, sampled_sampler_5, vec2<f32>(0.25, 0.25)); }
      case 6u: { return textureSample(sampled_texture_10, sampled_sampler_6, vec2<f32>(0.25, 0.25)); }
      case 7u: { return textureSample(sampled_texture_10, sampled_sampler_7, vec2<f32>(0.25, 0.25)); }
      case 8u: { return textureSample(sampled_texture_10, sampled_sampler_8, vec2<f32>(0.25, 0.25)); }
      case 9u: { return textureSample(sampled_texture_10, sampled_sampler_9, vec2<f32>(0.25, 0.25)); }
      case 10u: { return textureSample(sampled_texture_10, sampled_sampler_10, vec2<f32>(0.25, 0.25)); }
      case 11u: { return textureSample(sampled_texture_10, sampled_sampler_11, vec2<f32>(0.25, 0.25)); }
      case 12u: { return textureSample(sampled_texture_10, sampled_sampler_12, vec2<f32>(0.25, 0.25)); }
      case 13u: { return textureSample(sampled_texture_10, sampled_sampler_13, vec2<f32>(0.25, 0.25)); }
      case 14u: { return textureSample(sampled_texture_10, sampled_sampler_14, vec2<f32>(0.25, 0.25)); }
      case 15u: { return textureSample(sampled_texture_10, sampled_sampler_15, vec2<f32>(0.25, 0.25)); }
      default: { return vec4<f32>(1.0, 0.0, 1.0, 1.0); }
    } }
    case 11u: { switch smp {
      case 0u: { return textureSample(sampled_texture_11, sampled_sampler_0, vec2<f32>(0.25, 0.25)); }
      case 1u: { return textureSample(sampled_texture_11, sampled_sampler_1, vec2<f32>(0.25, 0.25)); }
      case 2u: { return textureSample(sampled_texture_11, sampled_sampler_2, vec2<f32>(0.25, 0.25)); }
      case 3u: { return textureSample(sampled_texture_11, sampled_sampler_3, vec2<f32>(0.25, 0.25)); }
      case 4u: { return textureSample(sampled_texture_11, sampled_sampler_4, vec2<f32>(0.25, 0.25)); }
      case 5u: { return textureSample(sampled_texture_11, sampled_sampler_5, vec2<f32>(0.25, 0.25)); }
      case 6u: { return textureSample(sampled_texture_11, sampled_sampler_6, vec2<f32>(0.25, 0.25)); }
      case 7u: { return textureSample(sampled_texture_11, sampled_sampler_7, vec2<f32>(0.25, 0.25)); }
      case 8u: { return textureSample(sampled_texture_11, sampled_sampler_8, vec2<f32>(0.25, 0.25)); }
      case 9u: { return textureSample(sampled_texture_11, sampled_sampler_9, vec2<f32>(0.25, 0.25)); }
      case 10u: { return textureSample(sampled_texture_11, sampled_sampler_10, vec2<f32>(0.25, 0.25)); }
      case 11u: { return textureSample(sampled_texture_11, sampled_sampler_11, vec2<f32>(0.25, 0.25)); }
      case 12u: { return textureSample(sampled_texture_11, sampled_sampler_12, vec2<f32>(0.25, 0.25)); }
      case 13u: { return textureSample(sampled_texture_11, sampled_sampler_13, vec2<f32>(0.25, 0.25)); }
      case 14u: { return textureSample(sampled_texture_11, sampled_sampler_14, vec2<f32>(0.25, 0.25)); }
      case 15u: { return textureSample(sampled_texture_11, sampled_sampler_15, vec2<f32>(0.25, 0.25)); }
      default: { return vec4<f32>(1.0, 0.0, 1.0, 1.0); }
    } }
    case 12u: { switch smp {
      case 0u: { return textureSample(sampled_texture_12, sampled_sampler_0, vec2<f32>(0.25, 0.25)); }
      case 1u: { return textureSample(sampled_texture_12, sampled_sampler_1, vec2<f32>(0.25, 0.25)); }
      case 2u: { return textureSample(sampled_texture_12, sampled_sampler_2, vec2<f32>(0.25, 0.25)); }
      case 3u: { return textureSample(sampled_texture_12, sampled_sampler_3, vec2<f32>(0.25, 0.25)); }
      case 4u: { return textureSample(sampled_texture_12, sampled_sampler_4, vec2<f32>(0.25, 0.25)); }
      case 5u: { return textureSample(sampled_texture_12, sampled_sampler_5, vec2<f32>(0.25, 0.25)); }
      case 6u: { return textureSample(sampled_texture_12, sampled_sampler_6, vec2<f32>(0.25, 0.25)); }
      case 7u: { return textureSample(sampled_texture_12, sampled_sampler_7, vec2<f32>(0.25, 0.25)); }
      case 8u: { return textureSample(sampled_texture_12, sampled_sampler_8, vec2<f32>(0.25, 0.25)); }
      case 9u: { return textureSample(sampled_texture_12, sampled_sampler_9, vec2<f32>(0.25, 0.25)); }
      case 10u: { return textureSample(sampled_texture_12, sampled_sampler_10, vec2<f32>(0.25, 0.25)); }
      case 11u: { return textureSample(sampled_texture_12, sampled_sampler_11, vec2<f32>(0.25, 0.25)); }
      case 12u: { return textureSample(sampled_texture_12, sampled_sampler_12, vec2<f32>(0.25, 0.25)); }
      case 13u: { return textureSample(sampled_texture_12, sampled_sampler_13, vec2<f32>(0.25, 0.25)); }
      case 14u: { return textureSample(sampled_texture_12, sampled_sampler_14, vec2<f32>(0.25, 0.25)); }
      case 15u: { return textureSample(sampled_texture_12, sampled_sampler_15, vec2<f32>(0.25, 0.25)); }
      default: { return vec4<f32>(1.0, 0.0, 1.0, 1.0); }
    } }
    case 13u: { switch smp {
      case 0u: { return textureSample(sampled_texture_13, sampled_sampler_0, vec2<f32>(0.25, 0.25)); }
      case 1u: { return textureSample(sampled_texture_13, sampled_sampler_1, vec2<f32>(0.25, 0.25)); }
      case 2u: { return textureSample(sampled_texture_13, sampled_sampler_2, vec2<f32>(0.25, 0.25)); }
      case 3u: { return textureSample(sampled_texture_13, sampled_sampler_3, vec2<f32>(0.25, 0.25)); }
      case 4u: { return textureSample(sampled_texture_13, sampled_sampler_4, vec2<f32>(0.25, 0.25)); }
      case 5u: { return textureSample(sampled_texture_13, sampled_sampler_5, vec2<f32>(0.25, 0.25)); }
      case 6u: { return textureSample(sampled_texture_13, sampled_sampler_6, vec2<f32>(0.25, 0.25)); }
      case 7u: { return textureSample(sampled_texture_13, sampled_sampler_7, vec2<f32>(0.25, 0.25)); }
      case 8u: { return textureSample(sampled_texture_13, sampled_sampler_8, vec2<f32>(0.25, 0.25)); }
      case 9u: { return textureSample(sampled_texture_13, sampled_sampler_9, vec2<f32>(0.25, 0.25)); }
      case 10u: { return textureSample(sampled_texture_13, sampled_sampler_10, vec2<f32>(0.25, 0.25)); }
      case 11u: { return textureSample(sampled_texture_13, sampled_sampler_11, vec2<f32>(0.25, 0.25)); }
      case 12u: { return textureSample(sampled_texture_13, sampled_sampler_12, vec2<f32>(0.25, 0.25)); }
      case 13u: { return textureSample(sampled_texture_13, sampled_sampler_13, vec2<f32>(0.25, 0.25)); }
      case 14u: { return textureSample(sampled_texture_13, sampled_sampler_14, vec2<f32>(0.25, 0.25)); }
      case 15u: { return textureSample(sampled_texture_13, sampled_sampler_15, vec2<f32>(0.25, 0.25)); }
      default: { return vec4<f32>(1.0, 0.0, 1.0, 1.0); }
    } }
    case 14u: { switch smp {
      case 0u: { return textureSample(sampled_texture_14, sampled_sampler_0, vec2<f32>(0.25, 0.25)); }
      case 1u: { return textureSample(sampled_texture_14, sampled_sampler_1, vec2<f32>(0.25, 0.25)); }
      case 2u: { return textureSample(sampled_texture_14, sampled_sampler_2, vec2<f32>(0.25, 0.25)); }
      case 3u: { return textureSample(sampled_texture_14, sampled_sampler_3, vec2<f32>(0.25, 0.25)); }
      case 4u: { return textureSample(sampled_texture_14, sampled_sampler_4, vec2<f32>(0.25, 0.25)); }
      case 5u: { return textureSample(sampled_texture_14, sampled_sampler_5, vec2<f32>(0.25, 0.25)); }
      case 6u: { return textureSample(sampled_texture_14, sampled_sampler_6, vec2<f32>(0.25, 0.25)); }
      case 7u: { return textureSample(sampled_texture_14, sampled_sampler_7, vec2<f32>(0.25, 0.25)); }
      case 8u: { return textureSample(sampled_texture_14, sampled_sampler_8, vec2<f32>(0.25, 0.25)); }
      case 9u: { return textureSample(sampled_texture_14, sampled_sampler_9, vec2<f32>(0.25, 0.25)); }
      case 10u: { return textureSample(sampled_texture_14, sampled_sampler_10, vec2<f32>(0.25, 0.25)); }
      case 11u: { return textureSample(sampled_texture_14, sampled_sampler_11, vec2<f32>(0.25, 0.25)); }
      case 12u: { return textureSample(sampled_texture_14, sampled_sampler_12, vec2<f32>(0.25, 0.25)); }
      case 13u: { return textureSample(sampled_texture_14, sampled_sampler_13, vec2<f32>(0.25, 0.25)); }
      case 14u: { return textureSample(sampled_texture_14, sampled_sampler_14, vec2<f32>(0.25, 0.25)); }
      case 15u: { return textureSample(sampled_texture_14, sampled_sampler_15, vec2<f32>(0.25, 0.25)); }
      default: { return vec4<f32>(1.0, 0.0, 1.0, 1.0); }
    } }
    case 15u: { switch smp {
      case 0u: { return textureSample(sampled_texture_15, sampled_sampler_0, vec2<f32>(0.25, 0.25)); }
      case 1u: { return textureSample(sampled_texture_15, sampled_sampler_1, vec2<f32>(0.25, 0.25)); }
      case 2u: { return textureSample(sampled_texture_15, sampled_sampler_2, vec2<f32>(0.25, 0.25)); }
      case 3u: { return textureSample(sampled_texture_15, sampled_sampler_3, vec2<f32>(0.25, 0.25)); }
      case 4u: { return textureSample(sampled_texture_15, sampled_sampler_4, vec2<f32>(0.25, 0.25)); }
      case 5u: { return textureSample(sampled_texture_15, sampled_sampler_5, vec2<f32>(0.25, 0.25)); }
      case 6u: { return textureSample(sampled_texture_15, sampled_sampler_6, vec2<f32>(0.25, 0.25)); }
      case 7u: { return textureSample(sampled_texture_15, sampled_sampler_7, vec2<f32>(0.25, 0.25)); }
      case 8u: { return textureSample(sampled_texture_15, sampled_sampler_8, vec2<f32>(0.25, 0.25)); }
      case 9u: { return textureSample(sampled_texture_15, sampled_sampler_9, vec2<f32>(0.25, 0.25)); }
      case 10u: { return textureSample(sampled_texture_15, sampled_sampler_10, vec2<f32>(0.25, 0.25)); }
      case 11u: { return textureSample(sampled_texture_15, sampled_sampler_11, vec2<f32>(0.25, 0.25)); }
      case 12u: { return textureSample(sampled_texture_15, sampled_sampler_12, vec2<f32>(0.25, 0.25)); }
      case 13u: { return textureSample(sampled_texture_15, sampled_sampler_13, vec2<f32>(0.25, 0.25)); }
      case 14u: { return textureSample(sampled_texture_15, sampled_sampler_14, vec2<f32>(0.25, 0.25)); }
      case 15u: { return textureSample(sampled_texture_15, sampled_sampler_15, vec2<f32>(0.25, 0.25)); }
      default: { return vec4<f32>(1.0, 0.0, 1.0, 1.0); }
    } }
    default: { return vec4<f32>(1.0, 0.0, 1.0, 1.0); }
  }
}
@vertex fn vs_main(@builtin(vertex_index) i: u32) -> @builtin(position) vec4<f32> {
  let word = root.buffer_index * 64u + i * 2u;
  return vec4<f32>(bitcast<f32>(arena[word]), bitcast<f32>(arena[word + 1u]), 0.0, 1.0);
}
@fragment fn fs_main() -> @location(0) vec4<f32> { return sample_selected(root.texture_index, root.sampler_index); }
