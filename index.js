import tls from "node:tls"
import fs from "node:fs"
import os from "node:os"
import path from "node:path"

function log(msg) {
  try {
    const file = path.join(os.homedir(), ".config", "opencode", "qq-notify.log")
    fs.appendFileSync(file, `${new Date().toISOString()} ${msg}\n`, "utf8")
  } catch {}
}

function fileConfig() {
  try {
    const file = path.join(os.homedir(), ".config", "opencode", "qq-notify.json")
    return JSON.parse(fs.readFileSync(file, "utf8"))
  } catch (e) {
    log("[qq-notify] 读取配置文件失败: " + e)
    return undefined
  }
}

/**
 * Config resolution order:
 *   1. plugin options (from opencode.json `plugins[].options`)
 *   2. ~/.config/opencode/qq-notify.json
 */
function resolveConfig(options) {
  const f = fileConfig() || {}
  const o = options || {}
  return {
    host: o.host || f.host || "smtp.qq.com",
    port: Number(o.port ?? f.port ?? 465),
    user: o.user || f.user,
    authCode: o.authCode || f.authCode,
    to: o.to || f.to,
    fromName: o.fromName || f.fromName || "OpenCode",
    showCost: o.showCost ?? f.showCost ?? true,
    showContext: o.showContext ?? f.showContext ?? true,
  }
}

function encodeSubject(s) {
  return "=?UTF-8?B?" + Buffer.from(s, "utf8").toString("base64") + "?="
}

function formatDuration(ms) {
  if (!Number.isFinite(ms) || ms < 0) return "未知"
  const s = ms / 1000
  if (s < 60) return `${s.toFixed(1)} 秒`
  const m = Math.floor(s / 60)
  const rs = Math.round(s % 60)
  return `${m} 分 ${rs} 秒`
}

function fmtTokens(n) {
  if (!n || n < 0) return "0"
  if (n >= 1000000) return (n / 1000000).toFixed(2) + "M"
  if (n >= 1000) return (n / 1000).toFixed(1) + "K"
  return String(Math.round(n))
}

// RFC 5321: 单行不能超过 998 字节，超长则按字节折行（留余量用 900 字节）
function foldLines(text) {
  const LIMIT = 900
  const out = []
  for (const line of String(text).split("\n")) {
    let buf = Buffer.from(line, "utf8")
    if (buf.length <= LIMIT) {
      out.push(line)
      continue
    }
    while (buf.length > LIMIT) {
      let cut = LIMIT
      // 回退到 UTF-8 字符边界，避免切断多字节字符
      while (cut > 0 && (buf[cut] & 0xc0) === 0x80) cut--
      out.push(buf.subarray(0, cut).toString("utf8"))
      buf = buf.subarray(cut)
    }
    if (buf.length) out.push(buf.toString("utf8"))
  }
  return out.join("\n")
}

/** Minimal SMTP client (implicit TLS). */
function sendMail(cfg, subject, text) {
  return new Promise((resolve, reject) => {
    const sock = tls.connect({ host: cfg.host, port: cfg.port, servername: cfg.host })
    let buffer = ""
    let stage = 0
    let settled = false

    const fail = (msg) => {
      if (settled) return
      settled = true
      sock.destroy()
      reject(new Error(msg))
    }
    const done = () => {
      if (settled) return
      settled = true
      resolve()
    }
    const send = (line) => sock.write(line + "\r\n")

    sock.setTimeout(20000, () => fail("SMTP 超时"))
    sock.on("error", (e) => fail("SMTP 连接错误: " + e.message))
    sock.on("close", () => fail("SMTP 连接被关闭"))

    sock.on("data", (d) => {
      buffer += d.toString("utf8")
      let idx
      while ((idx = buffer.indexOf("\r\n")) >= 0) {
        const line = buffer.slice(0, idx)
        buffer = buffer.slice(idx + 2)
        handleLine(line)
      }
    })

    function handleLine(line) {
      const code = line.slice(0, 3)
      switch (stage) {
        case 0:
          if (code !== "220") return fail("greeting: " + line)
          send("EHLO qq-notify")
          stage = 1
          break
        case 1:
          if (line.startsWith("250-")) return
          if (code !== "250") return fail("ehlo: " + line)
          send("AUTH LOGIN")
          stage = 2
          break
        case 2:
          if (code !== "334") return fail("auth login: " + line)
          send(Buffer.from(cfg.user, "utf8").toString("base64"))
          stage = 3
          break
        case 3:
          if (code !== "334") return fail("auth user: " + line)
          send(Buffer.from(cfg.authCode, "utf8").toString("base64"))
          stage = 4
          break
        case 4:
          if (code !== "235") return fail("auth pass: " + line)
          send(`MAIL FROM:<${cfg.user}>`)
          stage = 5
          break
        case 5:
          if (code !== "250") return fail("mail from: " + line)
          send(`RCPT TO:<${cfg.to}>`)
          stage = 6
          break
        case 6:
          if (code !== "250") return fail("rcpt: " + line)
          send("DATA")
          stage = 7
          break
        case 7:
          if (code !== "354") return fail("data: " + line)
          const body = [
            `From: ${cfg.fromName} <${cfg.user}>`,
            `To: ${cfg.to}`,
            `Subject: ${encodeSubject(subject)}`,
            "MIME-Version: 1.0",
            "Content-Type: text/plain; charset=UTF-8",
            "",
            foldLines(text),
            ".",
          ].join("\r\n")
          sock.write(body + "\r\n")
          stage = 8
          break
        case 8:
          if (code !== "250") return fail("message: " + line)
          send("QUIT")
          stage = 9
          break
        case 9:
          if (code !== "221") return fail("quit: " + line)
          sock.end()
          done()
          break
      }
    }
  })
}

const plugin = {
  id: "codify_1.5",
  async setup(ctx) {
    const locationDir = ctx.location.directory
    const notified = new Set()
    const childSessions = new Set()
    const mainSessions = new Set()
    const controller = new AbortController()

    async function getReplySummary(sessionID) {
      try {
        const messages = await ctx.session.context({ sessionID })
        for (let i = messages.length - 1; i >= 0; i--) {
          const m = messages[i]
          if (m.type === "assistant" && Array.isArray(m.content)) {
            const text = m.content
              .filter((p) => p.type === "text" && typeof p.text === "string")
              .map((p) => p.text)
              .join("")
              .trim()
            if (text) {
              return text.replace(/\s+/g, " ")
            }
          }
        }
      } catch {}
      return ""
    }

    async function getLastUserMessageTime(sessionID) {
      try {
        const messages = await ctx.session.context({ sessionID })
        for (let i = messages.length - 1; i >= 0; i--) {
          const m = messages[i]
          if (m.type === "user" && m.time && typeof m.time.created === "number") {
            return m.time.created
          }
        }
      } catch {}
      return undefined
    }

    const replyStats = new Map()

    void (async () => {
      for await (const event of ctx.event.subscribe({ signal: controller.signal })) {
        try {
          const data = typeof event.data === "string" ? JSON.parse(event.data) : event.data
          if (!data) continue
          if (event.type !== "session.step.ended") continue

          const sid = data.sessionID
          const tk = data.tokens || {}
          if (sid) {
            const s = replyStats.get(sid) || { cost: 0, out: 0, reason: 0 }
            s.cost += data.cost || 0
            s.out += tk.output || 0
            s.reason += tk.reasoning || 0
            replyStats.set(sid, s)
          }

          if (data.finish !== "stop") continue
          if (event.location?.directory && event.location.directory !== locationDir) continue
          const sessionID = sid
          if (!sessionID) continue

          if (childSessions.has(sessionID)) continue
          if (!mainSessions.has(sessionID)) {
            const i = await ctx.session.get({ sessionID }).catch(() => undefined)
            if (i?.parentID) {
              childSessions.add(sessionID)
              continue
            }
            mainSessions.add(sessionID)
          }

          const cfg = resolveConfig(ctx.options)
          if (!cfg.user || !cfg.authCode || cfg.authCode.includes("<") || !cfg.to) {
            log("[qq-notify] 配置缺失或未填写授权码，跳过本次通知")
            continue
          }

          const messageID = data.assistantMessageID
          if (messageID) {
            if (notified.has(messageID)) continue
            notified.add(messageID)
            if (notified.size > 500) {
              const first = notified.values().next().value
              if (first) notified.delete(first)
            }
          }

          const userTime = await getLastUserMessageTime(sessionID)
          const elapsedMs = userTime ? event.created - userTime : NaN
          const duration = formatDuration(elapsedMs)

          const info = await ctx.session.get({ sessionID }).catch(() => undefined)
          const title = info?.title || sessionID
          const stats = replyStats.get(sessionID) || { cost: 0, out: 0, reason: 0 }
          replyStats.delete(sessionID)

          const lines = [
            "OpenCode 回复已就绪",
            "",
            `项目: ${locationDir}`,
            `会话: ${title}`,
            `回复耗时: ${duration}`,
          ]

          if (cfg.showCost) {
            lines.push(`本次成本: $${(stats.cost || 0).toFixed(4)}`)
            lines.push(`对话总成本: $${(info?.cost || 0).toFixed(4)}`)
          }

          if (cfg.showContext) {
            const used = (tk.input || 0) + ((tk.cache && tk.cache.read) || 0)
            let limit = 0
            if (info?.model) {
              const models = await ctx.model.list().catch(() => [])
              const m = models.find((x) => x.providerID === info.model.providerID && x.id === info.model.id)
              limit = (m && m.limit && m.limit.context) || 0
            }
            if (limit > 0) {
              const replyPct = ((stats.out + stats.reason) / limit) * 100
              const ctxPct = (used / limit) * 100
              lines.push(`本次上下文: ${replyPct.toFixed(2)}%`)
              lines.push(`对话上下文: ${ctxPct.toFixed(2)}% (${fmtTokens(used)}/${fmtTokens(limit)})`)
            } else {
              lines.push(`对话上下文: ${fmtTokens(used)} tokens`)
            }
          }

          const summary = await getReplySummary(sessionID)
          if (summary) lines.push(`回复摘要: ${summary}`)
          lines.push(`完成时间: ${new Date().toLocaleString("zh-CN")}`)
          const text = lines.join("\n")

          await sendMail(cfg, "OpenCode 回复已就绪", text)
          log(`[qq-notify] 已发送通知: ${title} (${duration})`)
        } catch (e) {
          log("[qq-notify] 处理事件失败: " + e)
        }
      }
    })()

    return () => controller.abort()
  },
}

export default plugin
