package com.ocutech.editor.effects

data class CubeLut(
    val size: Int,
    val rows: List<FloatArray>,
    val domainMin: FloatArray = floatArrayOf(0f, 0f, 0f),
    val domainMax: FloatArray = floatArrayOf(1f, 1f, 1f),
    val title: String? = null,
)

class CubeParseException(message: String) : IllegalArgumentException(message)

object CubeLutParser {
    fun parse(text: String): CubeLut {
        val rows = mutableListOf<FloatArray>()
        var size: Int? = null
        var title: String? = null
        var domainMin = floatArrayOf(0f, 0f, 0f)
        var domainMax = floatArrayOf(1f, 1f, 1f)

        text.lineSequence()
            .map { it.trim() }
            .filter { it.isNotEmpty() && !it.startsWith("#") }
            .forEach { line ->
                when {
                    line.startsWith("TITLE ") -> {
                        title = line.removePrefix("TITLE ").trim().trim('"')
                    }

                    line.startsWith("LUT_3D_SIZE ") -> {
                        size = line.removePrefix("LUT_3D_SIZE ").trim().toIntOrNull()
                            ?: throw CubeParseException("Invalid LUT_3D_SIZE value.")
                        if (size!! <= 1) {
                            throw CubeParseException("LUT_3D_SIZE must be greater than 1.")
                        }
                    }

                    line.startsWith("DOMAIN_MIN ") -> {
                        domainMin = parseDomain(line.removePrefix("DOMAIN_MIN ").trim())
                    }

                    line.startsWith("DOMAIN_MAX ") -> {
                        domainMax = parseDomain(line.removePrefix("DOMAIN_MAX ").trim())
                    }

                    else -> {
                        val values = line.split(Regex("\\s+"))
                        if (values.size != 3) {
                            throw CubeParseException("Invalid LUT row: '$line'")
                        }
                        val rgb = values.mapIndexed { channel, value ->
                            val parsed = value.toFloatOrNull()
                                ?: throw CubeParseException("Invalid float value '$value' in LUT row.")
                            if (!parsed.isFinite()) {
                                throw CubeParseException("LUT row contains non-finite value at channel $channel.")
                            }
                            parsed
                        }
                        rows += floatArrayOf(rgb[0], rgb[1], rgb[2])
                    }
                }
            }

        if (!domainMin.indices.all { domainMin[it].isFinite() } ||
            !domainMax.indices.all { domainMax[it].isFinite() }) {
            throw CubeParseException("DOMAIN_MIN / DOMAIN_MAX must contain finite float values.")
        }

        if (domainMin.indices.any { domainMax[it] <= domainMin[it] }) {
            throw CubeParseException("DOMAIN_MAX values must be greater than DOMAIN_MIN values.")
        }

        val resolvedSize = size ?: throw CubeParseException("Missing LUT_3D_SIZE declaration.")
        val expectedRows = resolvedSize * resolvedSize * resolvedSize
        if (rows.size != expectedRows) {
            throw CubeParseException(
                "LUT row count mismatch. Expected $expectedRows rows for size $resolvedSize, got ${rows.size}.",
            )
        }

        return CubeLut(
            size = resolvedSize,
            rows = rows,
            domainMin = domainMin,
            domainMax = domainMax,
            title = title,
        )
    }

    private fun parseDomain(value: String): FloatArray {
        val parts = value.split(Regex("\\s+"))
        if (parts.size != 3) {
            throw CubeParseException("DOMAIN values must contain exactly 3 numbers.")
        }
        val parsed = parts.mapIndexed { index, token ->
            token.toFloatOrNull()
                ?: throw CubeParseException("Invalid DOMAIN value '$token' at index $index.")
        }
        return floatArrayOf(parsed[0], parsed[1], parsed[2])
    }
}
