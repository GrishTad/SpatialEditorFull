package com.ocutech.editor.effects

import com.ocutech.editor.model.ClipEffectsSpec
import com.ocutech.editor.model.OverlaySpec

data class ResolvedEffects(
    val brightness: Float,
    val contrast: Float,
    val saturation: Float,
    val lutAssetPath: String?,
    val overlays: List<OverlaySpec>,
)

object EffectSpecBuilder {
    fun resolve(
        clipEffects: ClipEffectsSpec,
        overlaysById: Map<String, OverlaySpec>,
    ): ResolvedEffects {
        val resolvedOverlays = clipEffects.overlayIds
            .mapNotNull(overlaysById::get)
            .sortedWith(compareBy<OverlaySpec> { it.zIndex }.thenBy { it.id })
        return ResolvedEffects(
            brightness = (clipEffects.brightness ?: 0.0).toFloat(),
            contrast = (clipEffects.contrast ?: 1.0).toFloat(),
            saturation = (clipEffects.saturation ?: 1.0).toFloat(),
            lutAssetPath = clipEffects.lut,
            overlays = resolvedOverlays,
        )
    }
}
