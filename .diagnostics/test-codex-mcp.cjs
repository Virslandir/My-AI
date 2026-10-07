const {spawn} = require('node:child_process');
const readline = require('node:readline');
const path = require('node:path');
const root = path.resolve(__dirname, '..');
const executable = 'C:/Users/d.stefan.spi/AppData/Local/OpenAI/Codex/bin/979a96ce184041d1/codex.exe';
const proc = spawn(executable, ['app-server', ...(process.argv.includes('--plugin-only') ? ['-c','mcp_servers.filefinder.enabled=false'] : [])], {cwd:root, stdio:['pipe','pipe','pipe'], windowsHide:true});
let sequence = 0;
const pending = new Map();
let finishTurn;
const observedCalls=[];
proc.stderr.on('data', d => {if(/filefinder|MCP|mcp|error/i.test(d.toString())) process.stderr.write(d);});
readline.createInterface({input:proc.stdout}).on('line', line => {
  let message; try {message=JSON.parse(line);} catch {return;}
  if (pending.has(message.id)) {
    const {resolve,reject,timer}=pending.get(message.id); pending.delete(message.id); clearTimeout(timer);
    message.error ? reject(new Error(JSON.stringify(message.error))) : resolve(message.result);
  } else {
    if(message.method==='item/completed') {
      const item=message.params.item;
      if(item.type==='mcpToolCall') {observedCalls.push(item);console.log('MODEL_TOOL',JSON.stringify(item));}
      if(item.type==='agentMessage') console.log('MODEL_MESSAGE',item.text);
    }
    if(message.method==='turn/completed' && finishTurn) finishTurn(message.params.turn);
    if (/mcp|warning|error/i.test(message.method || '')) console.log(JSON.stringify(message));
  }
});
function rpc(method, params={}) {
  return new Promise((resolve,reject)=>{
    const id=++sequence;
    const timer=setTimeout(()=>{pending.delete(id);reject(new Error('Timeout: '+method));},45000);
    pending.set(id,{resolve,reject,timer});
    proc.stdin.write(JSON.stringify({jsonrpc:'2.0',id,method,params})+'\n');
  });
}
(async()=>{
  await rpc('initialize',{clientInfo:{name:'filefinder_diagnostics',version:'1.0.0'},capabilities:{experimentalApi:true}});
  proc.stdin.write(JSON.stringify({method:'initialized'})+'\n');
  const installed=await rpc('plugin/installed',{cwds:[root]});
  console.log('INSTALLED',JSON.stringify(installed.marketplaces?.flatMap(m=>m.plugins.filter(p=>p.name==='file-finder'))));
  if(process.argv.includes('--install')) console.log('INSTALL',JSON.stringify(await rpc('plugin/install',{marketplacePath:path.join(root,'.agents','plugins','marketplace.json'),pluginName:'file-finder'})));
  const thread=await rpc('thread/start',{cwd:root,ephemeral:true,approvalPolicy:'never', ...(process.argv.includes('--plugin-only') ? {config:{'mcp_servers.filefinder.enabled':false}} : {})});
  console.log('THREAD',thread.thread.id);
  const status=await rpc('mcpServerStatus/list',{threadId:thread.thread.id,limit:100});
  console.log('STATUS',JSON.stringify(status.data?.filter(s=>/filefinder/i.test(s.name))));
  for(const server of (status.data || []).filter(s=>/filefinder/i.test(s.name))) {
    if(server.runtimeStatus!=='connected' || server.pluginId!=='file-finder@myai-local') throw new Error('Bundled plugin did not connect');
    if(server.serverInfo?.name!=='FileFinder' || JSON.stringify(Object.keys(server.tools))!==JSON.stringify(['search_files'])) throw new Error('Initialization or advertised tools incorrect');
    const first=await rpc('mcpServer/tool/call',{threadId:thread.thread.id,server:server.name,tool:'search_files',arguments:{locations:[path.join(root,'MyAI.FileFinder.McpServer')],nameTerms:['Program'],extensions:['cs']}});
    if(first.structuredContent?.results?.[0]?.fullPath!==path.join(root,'MyAI.FileFinder.McpServer','Program.cs')) throw new Error('Expected Program.cs full path missing');
    console.log('CALL_PASS',server.name,JSON.stringify(first));
    const cases=[
      {name:'alternatives',args:{locations:[path.join(root,'MyAI.FileFinder.McpServer')],nameTerms:['NOT_A_MATCH','Program'],extensions:['.cs','txt']},expected:1},
      {name:'no matches',args:{locations:[path.join(root,'MyAI.FileFinder.McpServer')],nameTerms:['FILEFINDER_NO_SUCH_NAME_7392'],extensions:['cs']},expected:0},
      {name:'validation',args:{locations:[path.join(root,'MyAI.FileFinder.McpServer')]},error:true}
    ];
    for(const test of cases) {
      const result=await rpc('mcpServer/tool/call',{threadId:thread.thread.id,server:server.name,tool:'search_files',arguments:test.args});
      if(test.error ? !result.isError : result.structuredContent?.totalMatches!==test.expected) throw new Error('Failed case '+test.name+': '+JSON.stringify(result));
      console.log('CASE_PASS',test.name,JSON.stringify(result));
    }
  }
  if(!(status.data||[]).some(s=>s.pluginId==='file-finder@myai-local')) throw new Error('Plugin missing');
  if(process.argv.includes('--model')) {
    const done=new Promise(resolve=>{finishTurn=resolve;});
    await rpc('turn/start',{threadId:thread.thread.id,input:[{type:'mention',name:'FileFinder',path:'plugin://file-finder@myai-local'},{type:'text',text:'Use FileFinder to search only '+path.join(root,'MyAI.FileFinder.McpServer')+' for filenames containing Program and extension .cs. You must invoke its MCP search_files tool. Return the path from the tool result. Do not use shell or other file-search methods.',text_elements:[]}]});
    const timer=setTimeout(()=>{console.error('Model turn timed out');proc.kill();},180000);
    const turn=await done;clearTimeout(timer);
    if(turn.status!=='completed' || !observedCalls.some(c=>c.server==='filefinder' && c.tool==='search_files' && c.status==='completed' && c.pluginId==='file-finder@myai-local')) throw new Error('Model did not complete expected plugin call: '+JSON.stringify(turn));
    console.log('END_TO_END_PASS');
  }
})().catch(e=>{console.error(e);process.exitCode=1;}).finally(()=>{proc.stdin.end();proc.kill();});
