#!/usr/bin/env node
// Z.ai image-generation MCP server — zero-dependency (node >= 18).
//
// Wraps Z.ai's image generation API (POST /api/paas/v4/images/generations,
// model glm-image) as an MCP stdio server so agent frontends can generate
// images. Officially Z.ai ships a vision (image *understanding*) MCP server
// (@z_ai/mcp-server); this complements it with image *generation*.
//
// Credentials: ZAI_API_KEY (env) or a gitignored .env.local at the workspace
// root. Images are saved under .artifacts/zai-images/ and returned inline.

import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const SCRIPT_DIR = path.dirname(fileURLToPath(import.meta.url));
const WORKSPACE_ROOT = path.resolve(SCRIPT_DIR, "..", "..");
const IMAGES_DIR =
    process.env.ZAI_IMAGE_OUTPUT_DIR || path.join(WORKSPACE_ROOT, ".artifacts", "zai-images");
const DEFAULT_MODEL = process.env.ZAI_IMAGE_MODEL || "glm-image";
const DEFAULT_SIZE = process.env.ZAI_IMAGE_SIZE || "1024x1024";
const ENDPOINT =
    process.env.ZAI_IMAGE_ENDPOINT || "https://api.z.ai/api/paas/v4/images/generations";

function readEnvLocal(filePath) {
    const vars = {};
    let text;
    try {
        text = fs.readFileSync(filePath, "utf8");
    } catch {
        return vars;
    }
    for (const rawLine of text.split(/\r?\n/)) {
        const line = rawLine.trim();
        if (!line || line.startsWith("#")) continue;
        const eq = line.indexOf("=");
        if (eq <= 0) continue;
        const key = line.slice(0, eq).trim();
        let value = line.slice(eq + 1).trim();
        if (
            (value.startsWith('"') && value.endsWith('"')) ||
            (value.startsWith("'") && value.endsWith("'"))
        ) {
            value = value.slice(1, -1);
        }
        if (key) vars[key] = value;
    }
    return vars;
}

const envLocal = {
    ...readEnvLocal(path.join(WORKSPACE_ROOT, ".env.local")),
    ...readEnvLocal(path.join(process.env.HOME || "", ".unity-mcp", ".env.local")),
};

function resolveApiKey() {
    const key = process.env.ZAI_API_KEY || process.env.Z_AI_API_KEY || envLocal.ZAI_API_KEY || envLocal.Z_AI_API_KEY || "";
    if (!key) {
        throw new Error(
            "ZAI_API_KEY is not set. Add it to .env.local at the workspace root (see .env.local.example).",
        );
    }
    return key;
}

function slugify(prompt) {
    const slug = String(prompt)
        .toLowerCase()
        .replace(/[^a-z0-9]+/g, "-")
        .replace(/^-+|-+$/g, "")
        .slice(0, 48);
    return slug || "image";
}

async function generateImage({ prompt, size, model }) {
    const apiKey = resolveApiKey();
    const body = {
        model: model || DEFAULT_MODEL,
        prompt: String(prompt),
        size: size || DEFAULT_SIZE,
    };
    const response = await fetch(ENDPOINT, {
        method: "POST",
        headers: {
            "content-type": "application/json",
            authorization: `Bearer ${apiKey}`,
        },
        body: JSON.stringify(body),
    });
    const text = await response.text();
    if (!response.ok) {
        throw new Error(`Z.ai image API ${response.status}: ${text.slice(0, 500)}`);
    }
    let payload;
    try {
        payload = JSON.parse(text);
    } catch {
        throw new Error(`Z.ai image API returned non-JSON: ${text.slice(0, 200)}`);
    }
    const item = payload?.data?.[0];
    if (!item) {
        throw new Error(`Z.ai image API returned no data: ${text.slice(0, 300)}`);
    }
    if (item.b64_json) {
        return { base64: item.b64_json, mimeType: "image/png" };
    }
    if (item.url) {
        const imageResponse = await fetch(item.url);
        if (!imageResponse.ok) {
            throw new Error(`Failed to download generated image: ${imageResponse.status}`);
        }
        const buffer = Buffer.from(await imageResponse.arrayBuffer());
        return {
            base64: buffer.toString("base64"),
            mimeType: imageResponse.headers.get("content-type") || "image/png",
        };
    }
    throw new Error(`Z.ai image API returned neither b64_json nor url: ${text.slice(0, 300)}`);
}

function saveImage(base64, prompt) {
    fs.mkdirSync(IMAGES_DIR, { recursive: true });
    const stamp = new Date().toISOString().replace(/[:.]/g, "-");
    const filePath = path.join(IMAGES_DIR, `${stamp}-${slugify(prompt)}.png`);
    fs.writeFileSync(filePath, Buffer.from(base64, "base64"), { mode: 0o600 });
    return filePath;
}

const TOOL_DEFINITIONS = [
    {
        name: "zai_generate_image",
        description:
            "Generate an image with Z.ai's glm-image model from a text prompt. Returns the PNG inline and saves it under .artifacts/zai-images/.",
        inputSchema: {
            type: "object",
            properties: {
                prompt: { type: "string", description: "What to render." },
                size: {
                    type: "string",
                    description: "Image dimensions, e.g. 1024x1024 (default), 1152x864, 864x1152.",
                },
                model: {
                    type: "string",
                    description: "Z.ai image model id (default glm-image).",
                },
            },
            required: ["prompt"],
            additionalProperties: false,
        },
    },
];

async function callTool(name, args) {
    if (name !== "zai_generate_image") {
        return {
            content: [{ type: "text", text: `Unknown tool: ${name}` }],
            isError: true,
        };
    }
    const prompt = String(args?.prompt || "").trim();
    if (!prompt) {
        return {
            content: [{ type: "text", text: "prompt is required." }],
            isError: true,
        };
    }
    const { base64, mimeType } = await generateImage({
        prompt,
        size: args?.size,
        model: args?.model,
    });
    let savedPath = "";
    try {
        savedPath = saveImage(base64, prompt);
    } catch {
        savedPath = "(not saved)";
    }
    return {
        content: [
            {
                type: "text",
                text: `Generated image for: "${prompt}". Saved: ${savedPath}`,
            },
            { type: "image", data: base64, mimeType },
        ],
        isError: false,
    };
}

function handleJsonRpcMessage(message) {
    const { id, method, params } = message || {};
    if (method === "initialize") {
        const requested = String(params?.protocolVersion || "");
        const supported = new Set(["2025-06-18", "2025-11-25"]);
        return {
            jsonrpc: "2.0",
            id,
            result: {
                protocolVersion: supported.has(requested) ? requested : "2025-06-18",
                capabilities: { tools: { listChanged: false } },
                serverInfo: { name: "zai-image", version: "1.0.0" },
            },
        };
    }
    if (method === "tools/list") {
        return { jsonrpc: "2.0", id, result: { tools: TOOL_DEFINITIONS } };
    }
    if (method === "tools/call") {
        return callTool(String(params?.name || ""), params?.arguments || {})
            .then((result) => ({ jsonrpc: "2.0", id, result }))
            .catch((error) => ({
                jsonrpc: "2.0",
                id,
                result: {
                    content: [{ type: "text", text: error?.message || String(error) }],
                    isError: true,
                },
            }));
    }
    if (method === "ping") {
        return { jsonrpc: "2.0", id, result: {} };
    }
    if (typeof id === "undefined") {
        return null;
    }
    return {
        jsonrpc: "2.0",
        id,
        error: { code: -32601, message: `Method not found: ${method}` },
    };
}

function writeMessage(message) {
    process.stdout.write(`${JSON.stringify(message)}\n`);
}

let buffer = "";
let inFlight = 0;
let stdinClosed = false;
let exiting = false;
const maybeExit = () => {
    if (stdinClosed && !exiting && inFlight === 0) {
        exiting = true;
        process.exit(0);
    }
};
process.stdin.setEncoding("utf8");
process.stdin.on("data", (chunk) => {
    buffer += chunk;
    let newline;
    while ((newline = buffer.indexOf("\n")) !== -1) {
        const line = buffer.slice(0, newline).trim();
        buffer = buffer.slice(newline + 1);
        if (!line) continue;
        let message;
        try {
            message = JSON.parse(line);
        } catch (error) {
            writeMessage({
                jsonrpc: "2.0",
                id: null,
                error: { code: -32700, message: `Parse error: ${error.message}` },
            });
            continue;
        }
        const response = handleJsonRpcMessage(message);
        if (response) {
            inFlight += 1;
            Promise.resolve(response)
                .then(writeMessage)
                .catch((error) =>
                    writeMessage({
                        jsonrpc: "2.0",
                        id: message?.id ?? null,
                        error: { code: -32603, message: error?.message || String(error) },
                    }),
                )
                .finally(() => {
                    inFlight -= 1;
                    maybeExit();
                });
        }
    }
});
process.stdin.resume();
process.stdin.on("end", () => {
    stdinClosed = true;
    setTimeout(() => {
        exiting = true;
        process.exit(0);
    }, 15000);
    maybeExit();
});
