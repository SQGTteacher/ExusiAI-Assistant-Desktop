const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const crypto = require('node:crypto');
const publish = require('./publish_release.cjs');

function fixture(t, options = {}) {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'exusiai-release-'));
  t.after(() => fs.rmSync(directory, { recursive: true, force: true }));
  const version = '0.1.0-developer-beta';
  const archive = `ExusiAI-Assistant-Desktop-${version}-win-x64.zip`;
  const bytes = Buffer.from('validated archive');
  const sha256 = crypto.createHash('sha256').update(bytes).digest('hex');
  const context = { ref: 'refs/heads/main', eventName: 'push', sha: 'a'.repeat(40), repo: { owner: 'owner', repo: 'repo' } };
  const metadata = { version, tag: `v${version}`, name: 'Developer beta', prerelease: true, source_commit: context.sha, archive, sha256, ...options.metadata };
  for (const [name, value] of Object.entries({
    'release-metadata.json': JSON.stringify(metadata), [archive]: bytes,
    [`${archive}.sha256`]: `${sha256}  ${archive}\n`, 'BUILD-INFO.json': '{}',
    'RELEASE-NOTES.md': '请勿使用此版本用于生产环境，仅做开发者参考。',
  })) fs.writeFileSync(path.join(directory, name), value);
  const calls = [];
  const notFound = () => { throw Object.assign(new Error('Not found'), { status: 404 }); };
  const operation = name => async args => {
    calls.push([name, args]);
    if (name === 'upload' && options.uploadFailure) throw new Error('Upload interrupted');
    return { data: { id: 1, html_url: 'https://github.com/owner/repo/releases/tag/test' } };
  };
  const github = {
    rest: {
      repos: {
        getReleaseByTag: options.existing ? async () => ({ data: options.existing }) : notFound,
        createRelease: operation('create'), listReleaseAssets: () => {},
        deleteReleaseAsset: operation('delete'), uploadReleaseAsset: operation('upload'), updateRelease: operation('publish'),
      },
      git: { getRef: options.tagSha ? async () => ({ data: { object: { sha: options.tagSha } } }) : notFound },
    },
    paginate: async () => options.assets || [],
  };
  return { directory, context, github, core: { info() {} }, calls, archive };
}

test('uploads all assets to a draft before publishing a non-latest prerelease', async t => {
  const f = fixture(t);
  await publish(f);
  assert.deepEqual(f.calls.map(c => c[0]), ['create', 'upload', 'upload', 'upload', 'publish']);
  assert.equal(f.calls[0][1].draft, true);
  assert.equal(f.calls.at(-1)[1].draft, false);
  for (const [, args] of [f.calls[0], f.calls.at(-1)]) {
    assert.equal(args.prerelease, true);
    assert.equal(args.make_latest, 'false');
  }
});
test('a failed upload never publishes the draft', async t => {
  const f = fixture(t, { uploadFailure: true });
  await assert.rejects(publish(f), /interrupted/);
  assert.equal(f.calls.some(c => c[0] === 'publish'), false);
});
test('PR builds cannot publish', async t => {
  const f = fixture(t);
  f.context.eventName = 'pull_request';
  await assert.rejects(publish(f), /main build/);
  assert.deepEqual(f.calls, []);
});
test('refuses an artifact from another commit', async t => {
  const f = fixture(t, { metadata: { source_commit: 'b'.repeat(40) } });
  await assert.rejects(publish(f), /metadata/);
  assert.deepEqual(f.calls, []);
});
test('refuses a damaged archive', async t => {
  const f = fixture(t);
  fs.appendFileSync(path.join(f.directory, f.archive), 'damaged');
  await assert.rejects(publish(f), /checksum/);
  assert.deepEqual(f.calls, []);
});
test('does not overwrite an already published release', async t => {
  const f = fixture(t, { existing: { target_commitish: 'a'.repeat(40), prerelease: true, draft: false } });
  await publish(f);
  assert.deepEqual(f.calls, []);
});
test('refuses to move a conflicting tag', async t => {
  const f = fixture(t, { tagSha: 'b'.repeat(40) });
  await assert.rejects(publish(f), /tag points/);
  assert.deepEqual(f.calls, []);
});
test('resumes only this commit\'s draft after an interrupted upload', async t => {
  const f = fixture(t, { existing: { id: 1, target_commitish: 'a'.repeat(40), prerelease: true, draft: true }, assets: [{ id: 7, name: 'BUILD-INFO.json' }] });
  await publish(f);
  assert.deepEqual(f.calls.map(c => c[0]), ['upload', 'upload', 'delete', 'upload', 'publish']);
});
