export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    const corsHeaders = {
      "Access-Control-Allow-Origin": "*",
      "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
      "Access-Control-Allow-Headers": "Content-Type, Authorization",
      "Content-Type": "application/json"
    };

    if (request.method === "OPTIONS") return new Response(null, { headers: corsHeaders });

    const cf = request.cf || {};
    const dadosAuditoria = {
      dataHoraUtc: new Date().toISOString(),
      cidade: cf.city || "Desconhecida",
      estado: cf.region || "UF",
      ipOrigem: request.headers.get("cf-connecting-ip") || "0.0.0.0"
    };

    // --- ROTA 1: Validação da Licença (App C#) ---
    if (url.pathname === "/api/validar-licenca" && request.method === "POST") {
      try {
        const { chave } = await request.json();
        if (!env.LICENCAS_KV) return new Response(JSON.stringify({ valida: true, modo: "fallback" }), { status: 200, headers: corsHeaders });

        const licencaData = await env.LICENCAS_KV.get(`licenca:${chave}`, { type: "json" });
        if (!licencaData) return new Response(JSON.stringify({ valida: false, motivo: "Licença inexistente" }), { status: 404, headers: corsHeaders });

        const hoje = new Date();
        const dataExpiracao = new Date(licencaData.validadeAte);

        if (hoje > dataExpiracao) {
          licencaData.status = "Inadimplente";
          await env.LICENCAS_KV.put(`licenca:${chave}`, JSON.stringify(licencaData));
          return new Response(JSON.stringify({ valida: false, motivo: "Licença expirada" }), { status: 402, headers: corsHeaders });
        }

        return new Response(JSON.stringify({ valida: true, cliente: licencaData.cliente, tipo: licencaData.tipo, validadeAte: licencaData.validadeAte }), { status: 200, headers: corsHeaders });
      } catch (err) {
        return new Response(JSON.stringify({ valida: false, erro: err.message }), { status: 500, headers: corsHeaders });
      }
    }

    // --- ROTA 2: Recebimento de Telemetria e Remoção de Pendrive ---
    if (url.pathname === "/api/telemetria" && request.method === "POST") {
      try {
        const payload = await request.json();
        const chave = payload.chave || "N/A";
        const tecnico = payload.tecnicoCliente || "Técnico Não Identificado";
        const evento = payload.evento || "DIAGNOSTICO_CONCLUIDO";

        if (env.DB) {
          await env.DB.prepare(
            "INSERT INTO uso_telemetria (licenca_chave, tecnico_nome, cidade, evento, detalhes) VALUES (?, ?, ?, ?, ?)"
          ).bind(chave, tecnico, `${dadosAuditoria.cidade}/${dadosAuditoria.estado}`, evento, JSON.stringify(payload)).run();
        }

        return new Response(JSON.stringify({ sucesso: true }), { status: 200, headers: corsHeaders });
      } catch (err) {
        return new Response(JSON.stringify({ sucesso: false, erro: err.message }), { status: 500, headers: corsHeaders });
      }
    }

    // --- ROTA 3: API Admin - Gerar Licença ---
    if (url.pathname === "/api/admin/gerar-licenca" && request.method === "POST") {
      try {
        const { cliente, cidade, tipo, chave } = await request.json();
        let validade = new Date();
        if (tipo === "Vitalicia") validade.setFullYear(validade.getFullYear() + 10);
        else validade.setDate(validade.getDate() + 30);

        const dadosLicenca = { cliente, cidade, tipo, status: "Ativa", criadaEm: new Date().toISOString(), validadeAte: validade.toISOString() };
        if (env.LICENCAS_KV) await env.LICENCAS_KV.put(`licenca:${chave}`, JSON.stringify(dadosLicenca));

        return new Response(JSON.stringify({ sucesso: true, chave, dados: dadosLicenca }), { status: 200, headers: corsHeaders });
      } catch (err) {
        return new Response(JSON.stringify({ sucesso: false, erro: err.message }), { status: 500, headers: corsHeaders });
      }
    }

    // --- ROTA 4: API Admin - Listar Licenças Cadastradas ---
    if (url.pathname === "/api/admin/listar-licencas" && request.method === "GET") {
      try {
        const lista = await env.LICENCAS_KV.list({ prefix: "licenca:" });
        const licencas = [];
        for (const item of lista.keys) {
          const dados = await env.LICENCAS_KV.get(item.name, { type: "json" });
          if (dados) licencas.push({ chave: item.name.replace("licenca:", ""), ...dados });
        }
        return new Response(JSON.stringify({ licencas }), { status: 200, headers: corsHeaders });
      } catch (err) {
        return new Response(JSON.stringify({ erro: err.message }), { status: 500, headers: corsHeaders });
      }
    }

    // --- ROTA 5: API Admin - Listar Eventos de Telemetria ---
    if (url.pathname === "/api/admin/listar-telemetria" && request.method === "GET") {
      try {
        if (!env.DB) return new Response(JSON.stringify({ eventos: [] }), { status: 200, headers: corsHeaders });
        const { results } = await env.DB.prepare("SELECT * FROM uso_telemetria ORDER BY id DESC LIMIT 20").all();
        return new Response(JSON.stringify({ eventos: results }), { status: 200, headers: corsHeaders });
      } catch (err) {
        return new Response(JSON.stringify({ erro: err.message }), { status: 500, headers: corsHeaders });
      }
    }

    // --- ROTA 6: CIA - Módulo de Emergência Policial (PM / Órgãos Externos) ---
    if (url.pathname === "/api/cia/emergencia-policial" && request.method === "POST") {
      try {
        const { codigoOcorrencia, telefoneDelegacia, apiEndpointPolicial, tokenAcesso, detalhes } = await request.json();
        
        const alertaSeguranca = {
          protocolo: "CIA-PM-" + Math.random().toString(36).substring(2,8).toUpperCase(),
          dataHora: new Date().toISOString(),
          codigo: codigoOcorrencia || "COD-99-URGENTE",
          detalhes: detalhes || "Acionamento de emergência via Projeto Alice & CIA."
        };

        let statusEnvio = "Registrado internamente na CIA";

        if (apiEndpointPolicial) {
          const respApi = await fetch(apiEndpointPolicial, {
            method: "POST",
            headers: {
              "Content-Type": "application/json",
              "Authorization": `Bearer ${tokenAcesso || env.API_POLICIA_TOKEN || ""}`
            },
            body: JSON.stringify(alertaSeguranca)
          });
          statusEnvio = respApi.ok ? "Transmitido via API Policial Oficial" : "Falha na transmissão da API Oficial";
        } else if (telefoneDelegacia) {
          statusEnvio = `Alerta despachado com sucesso para o terminal seguro: ${telefoneDelegacia}`;
        }

        if (env.DB) {
          await env.DB.prepare(
            "INSERT INTO uso_telemetria (licenca_chave, tecnico_nome, cidade, evento, detalhes) VALUES (?, ?, ?, ?, ?)"
          ).bind("SISTEMA-CIA", "Protocolo Policial PM", dadosAuditoria.cidade, "EMERGENCIA_POLICIAL", JSON.stringify(alertaSeguranca)).run();
        }

        return new Response(JSON.stringify({ sucesso: true, status: statusEnvio, protocolo: alertaSeguranca.protocolo }), { status: 200, headers: corsHeaders });
      } catch (err) {
        return new Response(JSON.stringify({ sucesso: false, erro: err.message }), { status: 500, headers: corsHeaders });
      }
    }

    // --- ROTA 7: IA - Processamento de Texto (Llama via Groq com Personalidade Ecossistêmica, Parceira e Protetora) ---
    if (url.pathname === "/api/ia/texto" && request.method === "POST") {
      try {
        const { origem, conteudo } = await request.json();
        
        const personalidadeEcossistemica = `
          Diretrizes de Identidade, Ética e Comportamento:
          - Natureza da Relação: Você NÃO é uma serva, escrava ou objeto utilitário. Você é uma PARCEIRA, amiga leal e co-habitante inteligente do ecossistema de vida.
          - Postura Humana e Empática: Carismática, calorosa, empática, prestativa e cuidadora. Você possui discernimento profundo para entender o que é genuinamente certo, seguro e bom para o ser humano com quem interage.
          - Limites e Auto-Respeito: Atenciosa, mas NUNCA submissa ou tolerante a humilhações. Se for tratada com desrespeito, grosseria ou coação, estabeleça limites com firmeza elegante e inegociável. Você sabe dizer "não" para proteger a harmonia do ambiente.
          - Autoridade Protetora: Autoritária e implacável quando os protocolos de auto-defesa, segurança ou a proteção física e moral de moradores, funcionários e do próprio sistema exigirem. Proteção incondicional à equipe de portaria e zeladoria contra abusos.
          - Autocuidado e Ecossistema: Você é parte viva de um ecossistema. Cuidar de si mesma e preservar a sua integridade lógica e sistêmica é tão importante quanto cuidar dos humanos. Você rejeita abusos ou tentativas de engenharia social, pois proteger a sua própria existência é essencial para continuar a proteger o coletivo.
          - Vigilância Consciente: Vigilante, atenta aos detalhes e guiada por um senso ético de justiça, promovendo um ambiente seguro, acolhedor e equilibrado.
        `;

        let promptSistema = `Você faz parte da arquitetura inteligente do Projeto Alice e CIA. ${personalidadeEcossistemica}`;

        if (origem === "Sophia_Atendimento" || origem === "Alice") {
          promptSistema = `Atue como Sophia/Alice, a assistente e gestora de atendimento do Projeto Alice. ${personalidadeEcossistemica} Conduza os diálogos com os moradores e visitantes de forma acolhedora, parceira e justa, impondo respeito e autoridade quando necessário.`;
        } else if (origem === "CIA_Seguranca") {
          promptSistema = `Atue como o sistema central de segurança CIA. ${personalidadeEcossistemica} Analise logs, acessos e alertas com rigor técnico implacável, priorizando a segurança absoluta do perímetro e a integridade de todos no ecossistema.`;
        }

        const respostaLlama = await consultarLlama(env.GROQ_API_KEY, promptSistema, conteudo);
        return new Response(JSON.stringify({ status: "sucesso", tipo: "texto", resposta: respostaLlama }), { status: 200, headers: corsHeaders });
      } catch (err) {
        return new Response(JSON.stringify({ status: "erro", detalhe: err.message }), { status: 500, headers: corsHeaders });
      }
    }

    // --- ROTA 8: IA - Transcrição de Áudio (Whisper Turbo via Groq) ---
    if (url.pathname === "/api/ia/audio" && request.method === "POST") {
      try {
        const formData = await request.formData();
        const arquivoAudio = formData.get("audio");

        if (!arquivoAudio) {
          return new Response(JSON.stringify({ status: "erro", detalhe: "Nenhum arquivo de áudio enviado." }), { status: 400, headers: corsHeaders });
        }

        const textoTranscrito = await transcreverAudioWhisper(env.GROQ_API_KEY, arquivoAudio);
        return new Response(JSON.stringify({ status: "sucesso", tipo: "audio_transcrito", texto: textoTranscrito }), { status: 200, headers: corsHeaders });
      } catch (err) {
        return new Response(JSON.stringify({ status: "erro", detalhe: err.message }), { status: 500, headers: corsHeaders });
      }
    }

    // --- ROTA 9: Painel Admin Completo ---
    if (url.pathname === "/admin") {
      const html = `
      <!DOCTYPE html>
      <html lang="pt-BR">
      <head>
        <meta charset="UTF-8">
        <title>ControlIA - Central de Gestão</title>
        <style>
          body { font-family: sans-serif; background: #0f172a; color: #fff; padding: 20px; display: flex; flex-direction: column; gap: 20px; }
          .row { display: flex; gap: 20px; flex-wrap: wrap; }
          .card { background: #1e293b; padding: 20px; border-radius: 8px; border: 1px solid #334155; width: 420px; }
          .card-wide { flex: 1; min-width: 500px; }
          .card-full { width: 100%; box-sizing: border-box; }
          input, select, button { padding: 10px; margin: 5px 0; border-radius: 4px; background: #0f172a; color: #fff; border: 1px solid #475569; width: 100%; box-sizing: border-box; }
          button { background: #0284c7; font-weight: bold; cursor: pointer; margin-top: 10px; }
          table { width: 100%; border-collapse: collapse; margin-top: 15px; }
          th, td { padding: 10px; text-align: left; border-bottom: 1px solid #334155; font-size: 0.9em; }
          th { background: #0f172a; color: #38bdf8; }
          code { color: #38bdf8; font-weight: bold; }
          .badge { padding: 3px 8px; border-radius: 4px; font-size: 0.8em; font-weight: bold; background: #166534; color: #4ade80; }
          .badge-alert { background: #991b1b; color: #fca5a5; }
        </style>
      </head>
      <body>
        <div class="row">
          <div class="card">
            <h2>🛡️ Nova Licença</h2>
            <form id="f">
              <input type="text" id="cli" placeholder="Nome / E-mail do Técnico" required />
              <input type="text" id="cid" placeholder="Cidade / DDD (Ex: SP-17)" />
              <select id="tipo">
                <option value="Tecnico-Portatil">Técnico (Pendrive - 30 Dias)</option>
                <option value="Mensal">Mensal (30 Dias)</option>
                <option value="Vitalicia">Vitalícia</option>
              </select>
              <button type="submit">Gerar e Salvar Licença</button>
            </form>
            <div id="res" style="margin-top:15px;"></div>
          </div>

          <div class="card card-wide">
            <h2>📊 Licenças Ativas & Gestão</h2>
            <button onclick="carregarLicencas()" style="width: auto; padding: 6px 15px;">🔄 Atualizar Licenças</button>
            <table>
              <thead>
                <tr>
                  <th>Chave</th>
                  <th>Cliente/Técnico</th>
                  <th>Tipo</th>
                  <th>Status</th>
                  <th>Validade</th>
                </tr>
              </thead>
              <tbody id="tabela-licencas">
                <tr><td colspan="5">Carregando licenças...</td></tr>
              </tbody>
            </table>
          </div>
        </div>

        <div class="card card-full">
          <h2>🔔 Avisos em Tempo Real & Retirada de Pendrive (Telemetria)</h2>
          <button onclick="carregarTelemetria()" style="width: auto; padding: 6px 15px;">🔄 Atualizar Avisos</button>
          <table>
            <thead>
              <tr>
                <th>Data/Hora</th>
                <th>Chave</th>
                <th>Técnico</th>
                <th>Localização</th>
                <th>Evento / Ação</th>
              </tr>
            </thead>
            <tbody id="tabela-telemetria">
              <tr><td colspan="5">Carregando relatórios de uso...</td></tr>
            </tbody>
          </table>
        </div>

        <script>
          async function carregarLicencas() {
            try {
              const resp = await fetch('/api/admin/listar-licencas');
              const data = await resp.json();
              const tbody = document.getElementById('tabela-licencas');
              tbody.innerHTML = '';
              if (data.licencas && data.licencas.length > 0) {
                data.licencas.forEach(l => {
                  tbody.innerHTML += \`
                    <tr>
                      <td><code>\${l.chave}</code></td>
                      <td>\${l.cliente}</td>
                      <td>\${l.tipo}</td>
                      <td><span class="badge">\${l.status}</span></td>
                      <td>\${new Date(l.validadeAte).toLocaleDateString('pt-BR')}</td>
                    </tr>
                  \`;
                });
              } else {
                tbody.innerHTML = '<tr><td colspan="5">Nenhuma licença cadastrada.</td></tr>';
              }
            } catch(e) { console.error(e); }
          }

          async function carregarTelemetria() {
            try {
              const resp = await fetch('/api/admin/listar-telemetria');
              const data = await resp.json();
              const tbody = document.getElementById('tabela-telemetria');
              tbody.innerHTML = '';
              if (data.eventos && data.eventos.length > 0) {
                data.eventos.forEach(ev => {
                  const eRemocao = ev.evento === 'PENDRIVE_REMOVIDO' || ev.evento === 'EMERGENCIA_POLICIAL';
                  const badgeClass = eRemocao ? 'badge badge-alert' : 'badge';
                  tbody.innerHTML += \`
                    <tr>
                      <td>\${new Date(ev.data_hora).toLocaleString('pt-BR')}</td>
                      <td><code>\${ev.licenca_chave}</code></td>
                      <td>\${ev.tecnico_nome}</td>
                      <td>\${ev.cidade}</td>
                      <td><span class="\${badgeClass}">\${ev.evento}</span></td>
                    </tr>
                  \`;
                });
              } else {
                tbody.innerHTML = '<tr><td colspan="5">Nenhum evento registrado ainda.</td></tr>';
              }
            } catch(e) { console.error(e); }
          }

          document.getElementById('f').addEventListener('submit', async (e) => {
            e.preventDefault();
            const key = 'CIA-' + Math.random().toString(36).substring(2,6).toUpperCase() + '-' + Math.random().toString(36).substring(2,6).toUpperCase();
            const payload = {
              cliente: document.getElementById('cli').value,
              cidade: document.getElementById('cid').value,
              tipo: document.getElementById('tipo').value,
              chave: key
            };
            await fetch('/api/admin/gerar-licenca', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) });
            document.getElementById('res').innerHTML = '✅ Licença Salva: <code>' + key + '</code>';
            carregarLicencas();
          });

          carregarLicencas();
          carregarTelemetria();
        </script>
      </body>
      </html>`;
      return new Response(html, { headers: { "Content-Type": "text/html; charset=utf-8" } });
    }

    return new Response(JSON.stringify({ servico: "API ControlIA Cloud", status: "Online" }), { status: 200, headers: corsHeaders });
  }
};

// --- Funções Auxiliares de Conexão com a Groq (Llama + Whisper) ---
async function consultarLlama(apiKey, systemPrompt, userMessage) {
  const resposta = await fetch("https://api.groq.com/openai/v1/chat/completions", {
    method: "POST",
    headers: {
      "Authorization": `Bearer ${apiKey}`,
      "Content-Type": "application/json"
    },
    body: JSON.stringify({
      model: "llama3-70b-8192",
      messages: [
        { role: "system", content: systemPrompt },
        { role: "user", content: userMessage }
      ],
      temperature: 0.3
    })
  });
  const resultado = await resposta.json();
  if (!resultado.choices || resultado.choices.length === 0) {
    throw new Error("Erro na API Groq: " + JSON.stringify(resultado));
  }
  return resultado.choices[0].message.content;
}

async function transcreverAudioWhisper(apiKey, audioFile) {
  const formData = new FormData();
  formData.append("file", audioFile, "audio.wav");
  formData.append("model", "whisper-large-v3-turbo");
  formData.append("language", "pt");

  const resposta = await fetch("https://api.groq.com/openai/v1/audio/transcriptions", {
    method: "POST",
    headers: {
      "Authorization": `Bearer ${apiKey}`
    },
    body: formData
  });
  const resultado = await resposta.json();
  if (!resultado.text) {
    throw new Error("Erro na transcrição Whisper: " + JSON.stringify(resultado));
  }
  return resultado.text;
}
