// ControlIA Cloud — Worker da CIA
// Só cuida de: licenças (técnico e máquina), avisos das CIAs e o painel do dono.
// Variáveis necessárias no Cloudflare:
//   LICENCAS_KV  (KV namespace)   DB (D1, tabela uso_telemetria)   ADMIN_TOKEN (Secret = a senha do painel)

const CORS = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
  "Access-Control-Allow-Headers": "Content-Type, Authorization",
  "Content-Type": "application/json"
};

const ALFABETO = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // sem I, O, 0, 1 (evita confusão)
const CHAVE_VALIDA = /^[A-Z0-9]{5}(-[A-Z0-9]{5}){2}$/; // mesmo formato que o app C# exige
const LIMITE_TELEMETRIA_BYTES = 8192;
const EVENTOS_DAS_CIAS = ["RELATORIO_MENSAL", "DIAGNOSTICO_CONCLUIDO", "PERIGO_CRITICO"];

const PLANOS = {
  "tecnico-30": { tipo: "Tecnico", plano: "30 dias", dias: 30 },
  "maquina-30": { tipo: "Maquina", plano: "Mensal", dias: 30 },
  "maquina-vitalicia": { tipo: "Maquina", plano: "Vitalícia", dias: 3650 }
};

export default {
  async fetch(request, env) {
    try {
      return await rotear(request, env);
    } catch (err) {
      return json({ erro: "Erro interno" }, 500);
    }
  },

  // Roda uma vez por dia se você ligar o Cron Trigger (veja o passo a passo).
  async scheduled(event, env, ctx) {
    ctx.waitUntil(verificarTecnicosExpirados(env));
  }
};

// ---------------------------------------------------------------- roteamento
async function rotear(request, env) {
  const url = new URL(request.url);
  const metodo = request.method;
  const caminho = url.pathname;

  if (metodo === "OPTIONS") return new Response(null, { headers: CORS });

  if (caminho === "/api/validar-licenca" && metodo === "POST") return validarLicenca(request, env);
  if (caminho === "/api/telemetria" && metodo === "POST") return receberAviso(request, env);

  if (caminho === "/admin" && metodo === "GET") {
    return new Response(PAGINA_ADMIN, {
      headers: {
        "Content-Type": "text/html; charset=utf-8",
        "X-Content-Type-Options": "nosniff",
        "Content-Security-Policy":
          "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; connect-src 'self'; frame-ancestors 'none'"
      }
    });
  }

  if (caminho.startsWith("/api/admin/")) {
    if (!autorizado(request, env)) return json({ erro: "Não autorizado" }, 401);

    if (caminho === "/api/admin/licencas" && metodo === "GET") return adminListarLicencas(env);
    if (caminho === "/api/admin/avisos" && metodo === "GET") return adminListarAvisos(env);
    if (caminho === "/api/admin/gerar-licenca" && metodo === "POST") return adminGerarLicenca(request, env, url);
    if (caminho === "/api/admin/revogar-licenca" && metodo === "POST") return adminRevogar(request, env, url);
    if (caminho === "/api/admin/liberar-maquina" && metodo === "POST") return adminLiberarMaquina(request, env, url);
    return json({ erro: "Rota não encontrada" }, 404);
  }

  return json({ servico: "API ControlIA Cloud", status: "Online" });
}

// ---------------------------------------------------------------- utilidades
function json(objeto, status = 200) {
  return new Response(JSON.stringify(objeto), { status, headers: CORS });
}

function normalizarChave(valor) {
  return String(valor || "").trim().toUpperCase();
}

async function lerJson(request) {
  try {
    return await request.json();
  } catch (e) {
    return {};
  }
}

function cortar(valor, max) {
  return String(valor === undefined || valor === null ? "" : valor).slice(0, max);
}

function autorizado(request, env) {
  const segredo = env.ADMIN_TOKEN;
  if (!segredo || String(segredo).length < 12) return false; // sem senha configurada = painel fechado
  const cabecalho = request.headers.get("Authorization") || "";
  const enviado = cabecalho.startsWith("Bearer ") ? cabecalho.slice(7) : "";
  return iguaisSemPressa(enviado, String(segredo));
}

function iguaisSemPressa(a, b) {
  if (a.length !== b.length) return false;
  let diferenca = 0;
  for (let i = 0; i < a.length; i++) diferenca |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return diferenca === 0;
}

function gerarChave() {
  const bytes = new Uint8Array(15);
  crypto.getRandomValues(bytes);
  let texto = "";
  for (let i = 0; i < 15; i++) {
    texto += ALFABETO[bytes[i] % ALFABETO.length];
    if (i === 4 || i === 9) texto += "-";
  }
  return texto;
}

function localizacao(request) {
  const cf = (request && request.cf) || {};
  return (cf.city || "Desconhecida") + "/" + (cf.region || "UF");
}

async function registrarEvento(env, chave, nome, cidade, evento, detalhes) {
  if (!env.DB) return;
  try {
    await env.DB.prepare(
      "INSERT INTO uso_telemetria (licenca_chave, tecnico_nome, cidade, evento, detalhes) VALUES (?, ?, ?, ?, ?)"
    ).bind(cortar(chave, 40), cortar(nome, 120), cortar(cidade, 80), cortar(evento, 40), cortar(detalhes, 2000)).run();
  } catch (e) {
    // Falha ao registrar não pode derrubar a resposta.
  }
}

function ehTecnico(licenca) {
  return licenca.tipo === "Tecnico" || licenca.tipo === "Tecnico-Portatil";
}

async function salvarLicenca(env, chave, licenca) {
  await env.LICENCAS_KV.put("licenca:" + chave, JSON.stringify(licenca));
}

// ---------------------------------------------------------------- app: validar licença
async function validarLicenca(request, env) {
  const corpo = await lerJson(request);
  const chave = normalizarChave(corpo.chave);

  if (!CHAVE_VALIDA.test(chave)) return json({ valida: false, motivo: "Formato de chave inválido" }, 400);
  // Sem banco de licenças, NUNCA responde "válida".
  if (!env.LICENCAS_KV) return json({ valida: false, motivo: "Servidor sem banco de licenças" }, 503);

  const licenca = await env.LICENCAS_KV.get("licenca:" + chave, { type: "json" });
  if (!licenca) return json({ valida: false, motivo: "Licença inexistente" }, 404);
  if (licenca.status === "Revogada") return json({ valida: false, motivo: "Licença revogada" }, 403);

  const agora = new Date();

  if (agora > new Date(licenca.validadeAte)) {
    let mudou = false;
    if (licenca.status !== "Expirada") { licenca.status = "Expirada"; mudou = true; }
    if (ehTecnico(licenca) && !licenca.perdaNotificada) {
      licenca.perdaNotificada = true;
      mudou = true;
      await registrarEvento(env, chave, licenca.cliente, localizacao(request), "TECNICO_PERDEU_LICENCA", "Licença de técnico expirou");
    }
    if (mudou) await salvarLicenca(env, chave, licenca);
    return json({ valida: false, motivo: "Licença expirada" }, 402);
  }

  let precisaSalvar = false;

  // Licença de máquina: a CIA vive em UMA máquina só.
  if (licenca.tipo === "Maquina") {
    const maquinaId = cortar(corpo.maquinaId, 200).trim();
    if (!maquinaId) return json({ valida: false, motivo: "Identificação da máquina ausente" }, 400);

    if (!licenca.maquinaId) {
      licenca.maquinaId = maquinaId;
      licenca.ativadaEm = agora.toISOString();
      precisaSalvar = true;
    } else if (licenca.maquinaId !== maquinaId) {
      await registrarEvento(env, chave, licenca.cliente, localizacao(request), "TENTATIVA_OUTRA_MAQUINA",
        "Licença já pertence a outra máquina");
      return json({ valida: false, motivo: "Esta licença já pertence a outra máquina" }, 409);
    }
  }

  const ultimo = licenca.ultimoContato ? new Date(licenca.ultimoContato) : null;
  if (!ultimo || agora - ultimo > 24 * 60 * 60 * 1000) {
    licenca.ultimoContato = agora.toISOString();
    precisaSalvar = true;
  }
  if (precisaSalvar) await salvarLicenca(env, chave, licenca);

  return json({ valida: true, cliente: licenca.cliente, tipo: licenca.tipo, validadeAte: licenca.validadeAte });
}

// ---------------------------------------------------------------- app: avisos das CIAs (1x por mês etc.)
async function receberAviso(request, env) {
  const texto = await request.text();
  if (texto.length > LIMITE_TELEMETRIA_BYTES) return json({ sucesso: false, erro: "Aviso grande demais" }, 413);

  let aviso;
  try {
    aviso = JSON.parse(texto);
  } catch (e) {
    return json({ sucesso: false, erro: "JSON inválido" }, 400);
  }
  if (!aviso || typeof aviso !== "object") return json({ sucesso: false, erro: "Aviso inválido" }, 400);

  const chave = normalizarChave(aviso.chave);
  let nome = cortar(aviso.tecnicoCliente, 120) || "Não identificado";
  let verificado = false;

  if (CHAVE_VALIDA.test(chave) && env.LICENCAS_KV) {
    const licenca = await env.LICENCAS_KV.get("licenca:" + chave, { type: "json" });
    if (licenca) {
      const mesmaMaquina = licenca.tipo !== "Maquina" || !licenca.maquinaId ||
        licenca.maquinaId === cortar(aviso.maquinaId, 200).trim();
      if (mesmaMaquina && licenca.status !== "Revogada") {
        verificado = true;
        nome = licenca.cliente;
        const ultimo = licenca.ultimoContato ? new Date(licenca.ultimoContato) : null;
        if (!ultimo || new Date() - ultimo > 24 * 60 * 60 * 1000) {
          licenca.ultimoContato = new Date().toISOString();
          await salvarLicenca(env, chave, licenca);
        }
      }
    }
  }

  const pedido = String(aviso.evento || "DIAGNOSTICO_CONCLUIDO").toUpperCase();
  const evento = EVENTOS_DAS_CIAS.includes(pedido) ? pedido : "OUTRO";

  const detalhes = JSON.stringify({ verificado: verificado, dados: aviso });
  await registrarEvento(env, chave || "N/A", nome, localizacao(request), evento, detalhes);

  return json({ sucesso: true });
}

// ---------------------------------------------------------------- rotina diária (opcional)
async function verificarTecnicosExpirados(env) {
  if (!env.LICENCAS_KV) return;
  const agora = new Date();
  let cursor;
  do {
    const pagina = await env.LICENCAS_KV.list({ prefix: "licenca:", cursor: cursor });
    for (const item of pagina.keys) {
      const licenca = await env.LICENCAS_KV.get(item.name, { type: "json" });
      if (!licenca || !ehTecnico(licenca) || licenca.perdaNotificada) continue;
      if (agora > new Date(licenca.validadeAte)) {
        licenca.status = "Expirada";
        licenca.perdaNotificada = true;
        await env.LICENCAS_KV.put(item.name, JSON.stringify(licenca));
        await registrarEvento(env, item.name.replace("licenca:", ""), licenca.cliente, "Servidor",
          "TECNICO_PERDEU_LICENCA", "Licença de técnico expirou");
      }
    }
    cursor = pagina.list_complete ? undefined : pagina.cursor;
  } while (cursor);
}

// ---------------------------------------------------------------- painel do dono (API)
async function adminListarLicencas(env) {
  if (!env.LICENCAS_KV) return json({ licencas: [] });
  const licencas = [];
  let cursor;
  do {
    const pagina = await env.LICENCAS_KV.list({ prefix: "licenca:", cursor: cursor });
    for (const item of pagina.keys) {
      if (licencas.length >= 300) break;
      const dados = await env.LICENCAS_KV.get(item.name, { type: "json" });
      if (dados) licencas.push(Object.assign({ chave: item.name.replace("licenca:", "") }, dados));
    }
    cursor = pagina.list_complete || licencas.length >= 300 ? undefined : pagina.cursor;
  } while (cursor);
  return json({ licencas: licencas });
}

async function adminListarAvisos(env) {
  if (!env.DB) return json({ eventos: [] });
  const { results } = await env.DB.prepare("SELECT * FROM uso_telemetria ORDER BY id DESC LIMIT 100").all();
  return json({ eventos: results });
}

async function adminGerarLicenca(request, env, url) {
  if (!env.LICENCAS_KV) return json({ sucesso: false, erro: "LICENCAS_KV não configurado" }, 503);
  const corpo = await lerJson(request);
  const plano = PLANOS[corpo.plano];
  const cliente = cortar(corpo.cliente, 80).trim();
  if (!plano) return json({ sucesso: false, erro: "Plano inválido" }, 400);
  if (!cliente) return json({ sucesso: false, erro: "Informe o nome do cliente/técnico" }, 400);

  let chave = gerarChave();
  for (let i = 0; i < 5 && (await env.LICENCAS_KV.get("licenca:" + chave)); i++) chave = gerarChave();

  const validade = new Date();
  validade.setDate(validade.getDate() + plano.dias);

  const licenca = {
    cliente: cliente,
    cidade: cortar(corpo.cidade, 60).trim(),
    tipo: plano.tipo,
    plano: plano.plano,
    status: "Ativa",
    criadaEm: new Date().toISOString(),
    validadeAte: validade.toISOString()
  };
  await salvarLicenca(env, chave, licenca);
  return json({ sucesso: true, chave: chave, dados: licenca });
}

async function adminRevogar(request, env) {
  const chave = normalizarChave((await lerJson(request)).chave);
  const licenca = CHAVE_VALIDA.test(chave) ? await env.LICENCAS_KV.get("licenca:" + chave, { type: "json" }) : null;
  if (!licenca) return json({ sucesso: false, erro: "Licença não encontrada" }, 404);
  licenca.status = "Revogada";
  await salvarLicenca(env, chave, licenca);
  await registrarEvento(env, chave, licenca.cliente, "Painel", "LICENCA_REVOGADA", "Revogada pelo dono");
  return json({ sucesso: true });
}

async function adminLiberarMaquina(request, env) {
  const chave = normalizarChave((await lerJson(request)).chave);
  const licenca = CHAVE_VALIDA.test(chave) ? await env.LICENCAS_KV.get("licenca:" + chave, { type: "json" }) : null;
  if (!licenca) return json({ sucesso: false, erro: "Licença não encontrada" }, 404);
  delete licenca.maquinaId;
  delete licenca.ativadaEm;
  await salvarLicenca(env, chave, licenca);
  await registrarEvento(env, chave, licenca.cliente, "Painel", "MAQUINA_LIBERADA", "Vínculo com a máquina removido pelo dono");
  return json({ sucesso: true });
}

// ---------------------------------------------------------------- painel do dono (página)
// Obs.: a página monta tudo com textContent (nunca innerHTML), então nada vindo das CIAs vira código.
const PAGINA_ADMIN = `<!DOCTYPE html>
<html lang="pt-BR">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>ControlIA - Painel da CIA</title>
<style>
  body { font-family: Segoe UI, sans-serif; background: #0f172a; color: #fff; margin: 0; padding: 20px; }
  h1 { margin: 0 0 4px 0; } h2 { margin-top: 0; }
  .sub { color: #94a3b8; margin-bottom: 20px; }
  .row { display: flex; gap: 20px; flex-wrap: wrap; margin-bottom: 20px; }
  .card { background: #1e293b; padding: 20px; border-radius: 8px; border: 1px solid #334155; }
  .nova { width: 360px; } .larga { flex: 1; min-width: 520px; overflow-x: auto; }
  input, select, button { padding: 10px; margin: 5px 0; border-radius: 4px; background: #0f172a; color: #fff; border: 1px solid #475569; width: 100%; box-sizing: border-box; font-size: 14px; }
  button { background: #0284c7; font-weight: bold; cursor: pointer; border: none; }
  button.peq { width: auto; padding: 4px 10px; margin: 0 4px 0 0; font-size: 12px; }
  button.perigo { background: #b91c1c; } button.neutro { background: #475569; }
  table { width: 100%; border-collapse: collapse; margin-top: 12px; }
  th, td { padding: 8px; text-align: left; border-bottom: 1px solid #334155; font-size: 13px; vertical-align: top; }
  th { color: #38bdf8; } code { color: #38bdf8; font-weight: bold; }
  .badge { padding: 2px 8px; border-radius: 4px; font-size: 12px; font-weight: bold; background: #166534; color: #4ade80; }
  .alerta { background: #991b1b; color: #fca5a5; } .aviso { background: #854d0e; color: #fde047; }
  .msg { margin-top: 10px; min-height: 20px; } .erro { color: #fca5a5; }
  #app { display: none; }
</style>
</head>
<body>
<h1>Painel da CIA</h1>
<div class="sub">Licenças de técnicos e de máquinas, e avisos das CIAs.</div>

<div id="login" class="card nova">
  <h2>Entrar</h2>
  <input type="password" id="senha" placeholder="Senha do painel" autocomplete="current-password">
  <button id="btnEntrar">Entrar</button>
  <div class="msg erro" id="erroLogin"></div>
</div>

<div id="app">
  <div class="row">
    <div class="card nova">
      <h2>Nova licença</h2>
      <input type="text" id="cli" placeholder="Nome do técnico ou do cliente" maxlength="80">
      <input type="text" id="cid" placeholder="Cidade (ex.: Rio Preto/SP)" maxlength="60">
      <select id="plano">
        <option value="tecnico-30">Técnico (pendrive) - 30 dias</option>
        <option value="maquina-30">CIA de uma máquina - mensal</option>
        <option value="maquina-vitalicia">CIA de uma máquina - vitalícia</option>
      </select>
      <button id="btnGerar">Gerar licença</button>
      <div class="msg" id="resultado"></div>
    </div>
    <div class="card larga">
      <h2>Licenças</h2>
      <button class="peq neutro" id="btnLic">Atualizar</button>
      <table>
        <thead><tr><th>Chave</th><th>Cliente</th><th>Tipo</th><th>Status</th><th>Validade</th><th>Máquina</th><th>Último contato</th><th>Ações</th></tr></thead>
        <tbody id="tabLic"></tbody>
      </table>
    </div>
  </div>
  <div class="card">
    <h2>Avisos das CIAs</h2>
    <button class="peq neutro" id="btnAv">Atualizar</button>
    <table>
      <thead><tr><th>Data/hora</th><th>Chave</th><th>Quem</th><th>Local</th><th>Aviso</th></tr></thead>
      <tbody id="tabAv"></tbody>
    </table>
  </div>
</div>

<script>
var senha = "";

function el(tag, texto, classe) {
  var e = document.createElement(tag);
  if (texto !== undefined && texto !== null) e.textContent = String(texto);
  if (classe) e.className = classe;
  return e;
}

function pedir(caminho, metodo, corpo) {
  var opcoes = { method: metodo || "GET", headers: { "Authorization": "Bearer " + senha } };
  if (corpo) { opcoes.headers["Content-Type"] = "application/json"; opcoes.body = JSON.stringify(corpo); }
  return fetch(caminho, opcoes).then(function (r) {
    return r.json().then(function (dados) { return { status: r.status, dados: dados }; });
  });
}

function dataBr(valor) {
  if (!valor) return "-";
  var d = new Date(valor);
  return isNaN(d.getTime()) ? String(valor) : d.toLocaleString("pt-BR");
}

function diasDesde(valor) {
  if (!valor) return null;
  var d = new Date(valor);
  if (isNaN(d.getTime())) return null;
  return Math.floor((Date.now() - d.getTime()) / 86400000);
}

function classeStatus(status) {
  if (status === "Ativa") return "badge";
  if (status === "Expirada") return "badge aviso";
  return "badge alerta";
}

function classeAviso(evento) {
  if (evento === "RELATORIO_MENSAL" || evento === "DIAGNOSTICO_CONCLUIDO") return "badge";
  if (evento === "PERIGO_CRITICO" || evento === "TECNICO_PERDEU_LICENCA" || evento === "TENTATIVA_OUTRA_MAQUINA" || evento === "LICENCA_REVOGADA") return "badge alerta";
  return "badge aviso";
}

function botao(texto, classe, aoClicar) {
  var b = el("button", texto, "peq " + classe);
  b.addEventListener("click", aoClicar);
  return b;
}

function carregarLicencas() {
  return pedir("/api/admin/licencas").then(function (res) {
    var corpo = document.getElementById("tabLic");
    corpo.textContent = "";
    var lista = res.dados.licencas || [];
    if (lista.length === 0) {
      var vazia = el("tr"); var cel = el("td", "Nenhuma licença ainda."); cel.colSpan = 8; vazia.appendChild(cel); corpo.appendChild(vazia);
      return;
    }
    lista.forEach(function (l) {
      var tr = el("tr");
      var c1 = el("td"); c1.appendChild(el("code", l.chave)); tr.appendChild(c1);
      tr.appendChild(el("td", l.cliente));
      tr.appendChild(el("td", (l.tipo || "") + (l.plano ? " - " + l.plano : "")));
      var c4 = el("td"); c4.appendChild(el("span", l.status, classeStatus(l.status))); tr.appendChild(c4);
      tr.appendChild(el("td", dataBr(l.validadeAte)));
      tr.appendChild(el("td", l.tipo === "Maquina" ? (l.maquinaId ? "Presa a 1 máquina" : "Livre (ainda não ativada)") : "-"));
      var dias = diasDesde(l.ultimoContato);
      var textoContato = dias === null ? "nunca" : (dias === 0 ? "hoje" : "há " + dias + " dia(s)");
      var c7 = el("td", textoContato);
      if (l.tipo === "Maquina" && l.status === "Ativa" && dias !== null && dias > 40) c7.className = "erro";
      tr.appendChild(c7);
      var c8 = el("td");
      if (l.status !== "Revogada") {
        c8.appendChild(botao("Revogar", "perigo", function () {
          if (confirm("Revogar a licença " + l.chave + "?")) {
            pedir("/api/admin/revogar-licenca", "POST", { chave: l.chave }).then(function () { carregarLicencas(); carregarAvisos(); });
          }
        }));
      }
      if (l.tipo === "Maquina" && l.maquinaId) {
        c8.appendChild(botao("Liberar máquina", "neutro", function () {
          if (confirm("Soltar o vínculo desta licença com a máquina? Ela poderá ser ativada em outro PC.")) {
            pedir("/api/admin/liberar-maquina", "POST", { chave: l.chave }).then(function () { carregarLicencas(); carregarAvisos(); });
          }
        }));
      }
      tr.appendChild(c8);
      corpo.appendChild(tr);
    });
  });
}

function carregarAvisos() {
  return pedir("/api/admin/avisos").then(function (res) {
    var corpo = document.getElementById("tabAv");
    corpo.textContent = "";
    var lista = res.dados.eventos || [];
    if (lista.length === 0) {
      var vazia = el("tr"); var cel = el("td", "Nenhum aviso ainda."); cel.colSpan = 5; vazia.appendChild(cel); corpo.appendChild(vazia);
      return;
    }
    lista.forEach(function (ev) {
      var tr = el("tr");
      tr.appendChild(el("td", dataBr(ev.data_hora)));
      var c2 = el("td"); c2.appendChild(el("code", ev.licenca_chave)); tr.appendChild(c2);
      tr.appendChild(el("td", ev.tecnico_nome));
      tr.appendChild(el("td", ev.cidade));
      var c5 = el("td"); c5.appendChild(el("span", ev.evento, classeAviso(ev.evento))); tr.appendChild(c5);
      corpo.appendChild(tr);
    });
  });
}

document.getElementById("btnEntrar").addEventListener("click", function () {
  senha = document.getElementById("senha").value;
  document.getElementById("erroLogin").textContent = "";
  pedir("/api/admin/licencas").then(function (res) {
    if (res.status === 401) {
      document.getElementById("erroLogin").textContent = "Senha incorreta (ou o painel ainda não tem senha configurada).";
      senha = "";
      return;
    }
    document.getElementById("login").style.display = "none";
    document.getElementById("app").style.display = "block";
    carregarLicencas();
    carregarAvisos();
  }).catch(function () {
    document.getElementById("erroLogin").textContent = "Não consegui falar com o servidor.";
  });
});

document.getElementById("senha").addEventListener("keydown", function (e) {
  if (e.key === "Enter") document.getElementById("btnEntrar").click();
});

document.getElementById("btnGerar").addEventListener("click", function () {
  var saida = document.getElementById("resultado");
  saida.textContent = "";
  pedir("/api/admin/gerar-licenca", "POST", {
    cliente: document.getElementById("cli").value,
    cidade: document.getElementById("cid").value,
    plano: document.getElementById("plano").value
  }).then(function (res) {
    if (res.dados.sucesso) {
      saida.appendChild(document.createTextNode("Licença criada: "));
      saida.appendChild(el("code", res.dados.chave));
      document.getElementById("cli").value = "";
      carregarLicencas();
    } else {
      saida.appendChild(el("span", res.dados.erro || "Não foi possível gerar.", "erro"));
    }
  });
});

document.getElementById("btnLic").addEventListener("click", carregarLicencas);
document.getElementById("btnAv").addEventListener("click", carregarAvisos);
</script>
</body>
</html>`;
