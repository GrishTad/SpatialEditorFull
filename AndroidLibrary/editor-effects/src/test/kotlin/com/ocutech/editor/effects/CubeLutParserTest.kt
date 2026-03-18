package com.ocutech.editor.effects

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith

class CubeLutParserTest {
    @Test
    fun parsesValidCube() {
        val text = buildString {
            appendLine("TITLE \"unit\"")
            appendLine("LUT_3D_SIZE 2")
            appendLine("DOMAIN_MIN 0.0 0.0 0.0")
            appendLine("DOMAIN_MAX 1.0 1.0 1.0")
            appendLine("0.0 0.0 0.0")
            appendLine("1.0 0.0 0.0")
            appendLine("0.0 1.0 0.0")
            appendLine("1.0 1.0 0.0")
            appendLine("0.0 0.0 1.0")
            appendLine("1.0 0.0 1.0")
            appendLine("0.0 1.0 1.0")
            appendLine("1.0 1.0 1.0")
        }

        val lut = CubeLutParser.parse(text)
        assertEquals(2, lut.size)
        assertEquals(8, lut.rows.size)
        assertEquals("unit", lut.title)
    }

    @Test
    fun failsOnInvalidDomain() {
        val text = """
            LUT_3D_SIZE 2
            DOMAIN_MIN 1.0 1.0 1.0
            DOMAIN_MAX 0.0 0.0 0.0
            0.0 0.0 0.0
            1.0 0.0 0.0
            0.0 1.0 0.0
            1.0 1.0 0.0
            0.0 0.0 1.0
            1.0 0.0 1.0
            0.0 1.0 1.0
            1.0 1.0 1.0
        """.trimIndent()

        assertFailsWith<CubeParseException> {
            CubeLutParser.parse(text)
        }
    }

    @Test
    fun failsWhenLutSizeIsMissing() {
        val text = """
            DOMAIN_MIN 0.0 0.0 0.0
            DOMAIN_MAX 1.0 1.0 1.0
            0.0 0.0 0.0
        """.trimIndent()

        assertFailsWith<CubeParseException> {
            CubeLutParser.parse(text)
        }
    }

    @Test
    fun failsWhenRowCountDoesNotMatchSize() {
        val text = """
            LUT_3D_SIZE 2
            0.0 0.0 0.0
            1.0 0.0 0.0
        """.trimIndent()

        assertFailsWith<CubeParseException> {
            CubeLutParser.parse(text)
        }
    }

    @Test
    fun failsForNonFiniteValues() {
        val text = buildString {
            appendLine("LUT_3D_SIZE 2")
            appendLine("0.0 0.0 0.0")
            appendLine("NaN 0.0 0.0")
            appendLine("0.0 1.0 0.0")
            appendLine("1.0 1.0 0.0")
            appendLine("0.0 0.0 1.0")
            appendLine("1.0 0.0 1.0")
            appendLine("0.0 1.0 1.0")
            appendLine("1.0 1.0 1.0")
        }

        assertFailsWith<CubeParseException> {
            CubeLutParser.parse(text)
        }
    }
}
