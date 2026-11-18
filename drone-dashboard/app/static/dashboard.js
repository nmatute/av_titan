$(function () {
  const format = {
    pad2(number) {
      return String(number).padStart(2, "0");
    },

    clock(seconds) {
      const date = new Date(seconds * 1000);
      return [date.getHours(), date.getMinutes(), date.getSeconds()].map(format.pad2).join(":");
    },

    duration(seconds) {
      const hours = Math.floor(seconds / 3600);
      const minutes = Math.floor((seconds % 3600) / 60);
      return [hours, minutes, seconds % 60].map(format.pad2).join(":");
    },

    count(number) {
      return number.toLocaleString();
    },

    rssi(number) {
      return number.toFixed(4);
    },

    lastUpdate(timestamp) {
      if (timestamp === null) {
        return "—";
      }
      const secondsAgo = Math.floor((Date.now() - timestamp) / 1000);
      return secondsAgo < 2 ? "Just now" : secondsAgo + "s ago";
    },

    escapeHtml: DataTable.util.escapeHtml,
  };

  const POLL_MS = 1000;

  // Keep the API's row order until the user sorts a column.
  function createDetectionsTable() {
    return $("#detections").DataTable({
      order: [],
      paging: false,
      info: false,
      layout: { topEnd: { search: { placeholder: "Filter detections" } } },
      language: {
        emptyTable: "No active detections",
        zeroRecords: "No matching detections",
      },
      columns: [
        {
          data: "drone_id",
          className: "drone-cell ps-4 font-monospace fw-semibold",
          render: DataTable.render.text(),
          createdCell(cell, value, drone) {
            $(cell).toggleClass("band-24", drone.band === "2.4GHz");
          },
        },
        {
          data: "start_time",
          className: "font-monospace muted",
          // Show local time in the table and search, and sort by the timestamp.
          render: (start, type) => type === "display" || type === "filter" ? format.clock(start) : start,
        },
        {
          data: "duration",
          className: "font-monospace fw-semibold",
          render: (duration, type) => type === "display" || type === "filter" ? format.duration(duration) : duration,
        },
        {
          data: "band",
          render: (band, type) => type === "display"
            ? `<span class="badge band-badge font-monospace">${format.escapeHtml(band)}</span>`
            : format.escapeHtml(band),
        },
        { data: "classification", render: DataTable.render.text() },
        {
          data: "rssi",
          className: "text-end pe-4 font-monospace",
          render: (rssi, type) => type === "display"
            ? `<strong>${format.rssi(rssi)}</strong> <span class="muted small">dBm</span>`
            : rssi,
        },
      ],
    });
  }

  function updateDetectionsTable(table, active, displayTime) {
    const rows = active.map(drone => ({
      ...drone,
      duration: Math.max(0, displayTime - drone.start_time),
    }));
    // Refresh the rows on the existing table so its search and sort stay in place.
    table.clear().rows.add(rows).draw();
  }

  function renderNumbers(state) {
    const activeCount = state.active.length;
    const uniqueCount = Object.values(state.bands).reduce((sum, count) => sum + count, 0);

    $("#stat-active").text(format.pad2(activeCount));
    $("#stat-messages").text(format.count(state.messageCount));
    $("#stat-unique").text(format.pad2(uniqueCount));
    $("#stat-unique-active").text(activeCount + " currently active");
    $("#active-count").text(activeCount);
  }

  function renderHistogram(bins) {
    const biggest = Math.max(1, ...bins.map(bin => bin.count));
    const samples = bins.reduce((sum, bin) => sum + bin.count, 0);
    const bars = bins.map(({ count }) => {
      const height = (count / biggest) * 88;
      const topClass = count === biggest ? "hist-bar-top" : "";
      return `<div class="hist-col"><div class="hist-bar-area">
        <span class="hist-count font-monospace">${count}</span>
        <div class="hist-bar ${topClass}" style="height:${height}%"></div>
      </div></div>`;
    }).join("");
    const labels = bins.map(({ low }) => `<div class="hist-label font-monospace">${low}</div>`).join("");

    $("#histogram").html(
      `<div class="histogram">${bars}</div><div class="hist-labels">${labels}</div>`
    );
    $("#histogram-samples").text(format.count(samples) + " samples");
  }

  function renderCounts(elementId, counts, barClass) {
    const $container = $("#" + elementId);
    const entries = Object.entries(counts).sort((a, b) => b[1] - a[1]);
    if (entries.length === 0) {
      $container.html('<div class="muted small">No data yet</div>');
      return;
    }

    const total = entries.reduce((sum, [, count]) => sum + count, 0);
    const rows = entries.map(([name, count]) => {
      const percent = (count / total) * 100;
      return `<div class="mb-3">
        <div class="d-flex justify-content-between small mb-1">
          <span>${format.escapeHtml(name)}</span><strong>${count}</strong>
        </div>
        <div class="progress"><div class="progress-bar ${barClass}" style="width:${percent}%"></div></div>
      </div>`;
    }).join("");
    $container.html(rows);
  }

  function renderFeedControls(feedOn) {
    $("#feed-label").text(feedOn ? "Detection Feed On" : "Detection Feed Off");
    $("#feed-dot").toggleClass("off", !feedOn);
    $("#feed-button")
      .text(feedOn ? "Turn Off" : "Turn On")
      .toggleClass("btn-danger", feedOn)
      .toggleClass("btn-primary", !feedOn);
  }

  function startDashboard() {
    const table = createDetectionsTable();
    let state = { active: [], messageCount: 0, bands: {}, classes: {}, histogram: [] };
    let feedOn = true;
    let clockOffset = 0;
    let lastUpdate = null;
    let displayTime = 0;

    async function loadState() {
      try {
        // GET the latest dashboard snapshot as JSON.
        state = await $.ajax({
          url: "/api/state",
          method: "GET",
          dataType: "json",
        });
        lastUpdate = Date.now();
        // The server sends seconds and Date.now() returns milliseconds.
        clockOffset = state.serverTime - lastUpdate / 1000;
        $("#error-box").addClass("d-none");
      } catch {
        $("#error-box").removeClass("d-none");
      }
    }

    function render() {
      // Freeze detection durations when the feed is paused.
      if (feedOn) {
        displayTime = Math.floor(Date.now() / 1000 + clockOffset);
      }
      renderNumbers(state);
      updateDetectionsTable(table, state.active, displayTime);
      renderHistogram(state.histogram);
      renderCounts("bands", state.bands, "");
      renderCounts("classes", state.classes, "bar-blue");
      $("#last-update").text(format.lastUpdate(lastUpdate));
    }

    async function tick() {
      // Keep the last-update label current even when polling is paused.
      if (feedOn) {
        await loadState();
      }
      render();
    }

    $("#feed-button").on("click", () => {
      feedOn = !feedOn;
      renderFeedControls(feedOn);
      tick();
    });

    $("#clear-button").on("click", async () => {
      try {
        // Clear the server's detection data, then reload the dashboard snapshot.
        await $.ajax({
          url: "/api/clear",
          method: "POST",
        });
      } catch {
        // The refresh checks whether the API is reachable.
      }
      await loadState();
      render();
    });

    renderFeedControls(feedOn);
    tick();
    setInterval(tick, POLL_MS);
  }

  startDashboard();
});
