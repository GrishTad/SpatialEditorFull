package com.ocutech.editor.effects

import com.ocutech.editor.model.ClipEffectsSpec
import com.ocutech.editor.model.OverlaySpec
import kotlin.test.Test
import kotlin.test.assertEquals

class EffectSpecBuilderTest {
    @Test
    fun resolvesOverlayOrderDeterministicallyByZIndexThenId() {
        val overlays = mapOf(
            "logo-b" to OverlaySpec(
                id = "logo-b",
                uri = "content://demo/logo_b.png",
                x = 0.9,
                y = 0.1,
                scale = 0.2,
                zIndex = 2,
            ),
            "logo-a" to OverlaySpec(
                id = "logo-a",
                uri = "content://demo/logo_a.png",
                x = 0.8,
                y = 0.2,
                scale = 0.2,
                zIndex = 2,
            ),
            "base" to OverlaySpec(
                id = "base",
                uri = "content://demo/base.png",
                x = 0.5,
                y = 0.5,
                scale = 0.3,
                zIndex = 0,
            ),
        )

        val resolved = EffectSpecBuilder.resolve(
            clipEffects = ClipEffectsSpec(
                overlayIds = listOf("logo-b", "base", "logo-a"),
            ),
            overlaysById = overlays,
        )

        assertEquals(listOf("base", "logo-a", "logo-b"), resolved.overlays.map { it.id })
    }
}

