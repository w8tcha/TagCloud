/**
 * WordCloud - renders a list of words as a word cloud on a canvas element.
 *
 * Vanilla JavaScript port of the former jquery.wordcloud.js plugin (based on wordcloud by timdream).
 *
 * Usage:
 *   WordCloud(canvasElementOrId, { wordList: [['word', weight], ...], ... });
 *
 * Events dispatched on the canvas: wordcloudstart, wordcloudstop, wordcloudabort
 */
(function (window, document) {
    'use strict';

    var isSupported = (function () {
        var canvas = document.createElement('canvas');

        if (!canvas.getContext) {
            return false;
        }

        var ctx = canvas.getContext('2d');

        return !!(ctx && ctx.getImageData && ctx.fillText && Array.prototype.some && Array.prototype.push);
    }());

    // Find the minimum font size the browser will render, so smaller words can be drawn scaled up.
    var minimumFontSize = (function () {
        if (!isSupported) {
            return 0;
        }

        var ctx = document.createElement('canvas').getContext('2d'),
            size = 20,
            hanWidth,
            mWidth;

        while (size) {
            ctx.font = size.toString(10) + 'px sans-serif';

            if (ctx.measureText('Ｗ').width === hanWidth && ctx.measureText('m').width === mWidth) {
                return size + 1;
            }

            hanWidth = ctx.measureText('Ｗ').width;
            mWidth = ctx.measureText('m').width;
            size--;
        }

        return 0;
    }());

    // Zero-delay scheduling via postMessage (setTimeout(fn, 0) is clamped to >= 4ms by browsers).
    var setImmediate = (function () {
        if (!window.postMessage || !window.addEventListener) {
            return function (fn) {
                window.setTimeout(fn, 0);
            };
        }

        var queue = [],
            messageName = 'wordcloud-zero-timeout';

        window.addEventListener('message', function (event) {
            if (event.source === window && event.data === messageName) {
                event.stopPropagation();

                if (queue.length > 0) {
                    queue.shift()();
                }
            }
        }, true);

        return function (fn) {
            queue.push(fn);
            window.postMessage(messageName, '*');
        };
    }());

    var defaults = {
        fontFamily: '"Trebuchet MS", "Heiti TC", "Arial Unicode MS", "Droid Fallback Sans", sans-serif',
        gridSize: 8,
        ellipticity: 0.65,
        center: false,
        drawMask: false,
        maskColor: 'rgba(255,0,0,0.3)',
        maskGridWidth: 0.3,
        wordColor: 'random-dark',
        backgroundColor: 'rgba(255, 255, 255, 1)',
        wait: 0,
        abortThreshold: 0,
        abort: function () { },
        weightFactor: 2.1,
        minSize: 0,
        wordList: [],
        rotateRatio: 0.1,
        clearCanvas: true,
        fillBox: false,
        shape: 'star'
    };

    var shapes = {
        circle: function () {
            return 1;
        },
        cardioid: function (theta) {
            return 1 - Math.sin(theta);
        },
        diamond: function (theta) {
            var thetaDelta = theta % (2 * Math.PI / 4);
            return 1 / (Math.cos(thetaDelta) + Math.sin(thetaDelta));
        },
        'triangle-forward': function (theta) {
            var thetaDelta = theta % (2 * Math.PI / 3);
            return 1 / (Math.cos(thetaDelta) + Math.sqrt(3) * Math.sin(thetaDelta));
        },
        triangle: function (theta) {
            var thetaDelta = (theta + Math.PI * 3 / 2) % (2 * Math.PI / 3);
            return 1 / (Math.cos(thetaDelta) + Math.sqrt(3) * Math.sin(thetaDelta));
        },
        pentagon: function (theta) {
            var thetaDelta = (theta + 0.955) % (2 * Math.PI / 5);
            return 1 / (Math.cos(thetaDelta) + 0.726543 * Math.sin(thetaDelta));
        },
        star: function (theta) {
            var thetaDelta = (theta + 0.955) % (2 * Math.PI / 10);

            if ((theta + 0.955) % (2 * Math.PI / 5) - (2 * Math.PI / 10) >= 0) {
                return 1 / (Math.cos((2 * Math.PI / 10) - thetaDelta) + 3.07768 * Math.sin((2 * Math.PI / 10) - thetaDelta));
            }

            return 1 / (Math.cos(thetaDelta) + 3.07768 * Math.sin(thetaDelta));
        }
    };

    shapes.square = shapes.diamond;
    shapes['triangle-upright'] = shapes.triangle;

    function shuffle(array) {
        for (var i = array.length - 1; i > 0; i--) {
            var j = Math.floor(Math.random() * (i + 1)),
                temp = array[i];

            array[i] = array[j];
            array[j] = temp;
        }

        return array;
    }

    function trigger(element, name) {
        var event;

        if (typeof window.CustomEvent === 'function') {
            event = new window.CustomEvent(name);
        } else {
            event = document.createEvent('CustomEvent');
            event.initCustomEvent(name, false, false, null);
        }

        element.dispatchEvent(event);
    }

    function once(element, name, handler) {
        element.addEventListener(name, function listener(event) {
            element.removeEventListener(name, listener);
            handler(event);
        });
    }

    function randomColor(min) {
        return 'rgb(' +
            Math.floor(Math.random() * 128 + min).toString(10) + ',' +
            Math.floor(Math.random() * 128 + min).toString(10) + ',' +
            Math.floor(Math.random() * 128 + min).toString(10) + ')';
    }

    function WordCloud(element, options) {
        if (!isSupported) {
            return;
        }

        var canvas = typeof element === 'string' ? document.getElementById(element) : element;

        if (!canvas || canvas.nodeName.toLowerCase() !== 'canvas') {
            return;
        }

        var settings = {},
            key;

        for (key in defaults) {
            if (Object.prototype.hasOwnProperty.call(defaults, key)) {
                settings[key] = defaults[key];
            }
        }

        if (options) {
            for (key in options) {
                if (Object.prototype.hasOwnProperty.call(options, key)) {
                    settings[key] = options[key];
                }
            }
        }

        if (typeof settings.weightFactor !== 'function') {
            var factor = settings.weightFactor;

            settings.weightFactor = function (pt) {
                return pt * factor;
            };
        }

        if (typeof settings.shape !== 'function') {
            settings.shape = shapes[settings.shape] || shapes.circle;
        }

        settings.gridSize = Math.max(settings.gridSize, 4);

        var g = settings.gridSize,
            ctx,
            grid,
            ngx,
            ngy,
            diffChannel,
            bgPixel,
            escapeTime;

        var wordColor = function (word, weight, fontSize, radius, theta) {
            switch (settings.wordColor) {
                case 'random-dark':
                    return randomColor(0);
                case 'random-light':
                    return randomColor(128);
                default:
                    if (typeof settings.wordColor !== 'function') {
                        return settings.wordColor;
                    }

                    return settings.wordColor(word, weight, fontSize, radius, theta);
            }
        };

        var exceedTime = function () {
            return settings.abortThreshold > 0 && (new Date()).getTime() - escapeTime > settings.abortThreshold;
        };

        var getChannelData = function (data, x, y, w, h, c) {
            return data[(y * w + x) * 4 + c];
        };

        var isGridEmpty = function (imgData, x, y, w, h, skipDiffChannel) {
            var i = g,
                j,
                k;

            if (!isNaN(diffChannel) && !skipDiffChannel) {
                while (i--) {
                    j = g;

                    while (j--) {
                        if (getChannelData(imgData.data, x + i, y + j, w, h, diffChannel) !== bgPixel[diffChannel]) {
                            return false;
                        }
                    }
                }
            } else {
                while (i--) {
                    j = g;

                    while (j--) {
                        k = 4;

                        while (k--) {
                            if (getChannelData(imgData.data, x + i, y + j, w, h, k) !== bgPixel[k]) {
                                return false;
                            }
                        }
                    }
                }
            }

            return true;
        };

        var fillGrid = function (gx, gy, gw, gh) {
            var x = gw,
                y;

            if (settings.drawMask) {
                ctx.fillStyle = settings.maskColor;
            }

            while (x--) {
                y = gh;

                while (y--) {
                    grid[gx + x][gy + y] = false;

                    if (settings.drawMask) {
                        ctx.fillRect((gx + x) * g, (gy + y) * g, g - settings.maskGridWidth, g - settings.maskGridWidth);
                    }
                }
            }
        };

        var updateGrid = function (gx, gy, gw, gh, skipDiffChannel) {
            var x = gw,
                y;

            if (settings.drawMask) {
                ctx.fillStyle = settings.maskColor;
            }

            var imgData = ctx.getImageData(gx * g, gy * g, gw * g, gh * g);

            out: while (x--) {
                y = gh;

                while (y--) {
                    if (!isGridEmpty(imgData, x * g, y * g, gw * g, gh * g, skipDiffChannel)) {
                        grid[gx + x][gy + y] = false;

                        if (settings.drawMask) {
                            ctx.fillRect((gx + x) * g, (gy + y) * g, g - settings.maskGridWidth, g - settings.maskGridWidth);
                        }
                    }

                    if (exceedTime()) {
                        break out;
                    }
                }
            }
        };

        var canFitText = function (gx, gy, gw, gh) {
            if (gx < 0 || gy < 0 || gx + gw > ngx || gy + gh > ngy) {
                return false;
            }

            var x = gw,
                y;

            while (x--) {
                y = gh;

                while (y--) {
                    if (!grid[gx + x][gy + y]) {
                        return false;
                    }
                }
            }

            return true;
        };

        var putWord = function (word, weight) {
            var mu = 1,
                rotate = Math.random() < settings.rotateRatio,
                fontSize = settings.weightFactor(weight),
                w,
                h;

            if (fontSize <= settings.minSize) {
                return false;
            }

            if (fontSize < minimumFontSize) {
                mu = 2;

                while (mu * fontSize < minimumFontSize) {
                    mu += 2;
                }
            }

            ctx.font = (fontSize * mu).toString(10) + 'px ' + settings.fontFamily;

            var lineHeight = Math.max(fontSize * mu, ctx.measureText('m').width, ctx.measureText('Ｗ').width) / mu;

            if (rotate) {
                h = ctx.measureText(word).width / mu;
                w = lineHeight;

                if (/[Jgpqy]/.test(word)) {
                    w *= 3 / 2;
                }
            } else {
                w = ctx.measureText(word).width / mu;
                h = lineHeight;

                if (/[Jgpqy]/.test(word)) {
                    h *= 3 / 2;
                }
            }

            w += Math.floor(fontSize / 6);
            h += Math.floor(fontSize / 6);

            w = Math.ceil(w);
            h = Math.ceil(h);

            var gw = Math.ceil(w / g),
                gh = Math.ceil(h / g),
                center = settings.center ? [settings.center[0] / g, settings.center[1] / g] : [ngx / 2, ngy / 2],
                R = Math.floor(Math.sqrt(ngx * ngx + ngy * ngy)),
                T = ngx + ngy,
                r = R + 1,
                t,
                points;

            var drawAt = function (gxy) {
                if (!canFitText(gxy[0], gxy[1], gw, gh)) {
                    return false;
                }

                var x = Math.floor(gxy[0] * g + (gw * g - w) / 2),
                    y = Math.floor(gxy[1] * g + (gh * g - h) / 2);

                if (mu !== 1 || rotate) {
                    var fc = document.createElement('canvas');

                    fc.setAttribute('width', w * mu);
                    fc.setAttribute('height', h * mu);

                    var fctx = fc.getContext('2d');

                    fctx.fillStyle = settings.backgroundColor;
                    fctx.fillRect(0, 0, w * mu, h * mu);
                    fctx.fillStyle = wordColor(word, weight, fontSize, R - r, gxy[2]);
                    fctx.font = (fontSize * mu).toString(10) + 'px ' + settings.fontFamily;
                    fctx.textBaseline = 'top';

                    if (rotate) {
                        fctx.translate(0, h * mu);
                        fctx.rotate(-Math.PI / 2);
                    }

                    fctx.fillText(word, Math.floor(fontSize * mu / 6), 0);

                    ctx.clearRect(x, y, w, h);
                    ctx.drawImage(fc, x, y, w, h);
                } else {
                    ctx.font = fontSize.toString(10) + 'px ' + settings.fontFamily;
                    ctx.fillStyle = wordColor(word, weight, fontSize, R - r, gxy[2]);
                    ctx.fillText(word, gxy[0] * g + (gw * g - w) / 2, gxy[1] * g + (gh * g - h) / 2);
                }

                if (settings.fillBox) {
                    fillGrid(gxy[0], gxy[1], gw, gh);
                } else {
                    updateGrid(gxy[0], gxy[1], gw, gh);
                }

                return true;
            };

            while (r--) {
                t = T;
                points = [];

                while (t--) {
                    var rx = settings.shape(t / T * 2 * Math.PI);

                    points.push([
                        Math.floor(center[0] + (R - r) * rx * Math.cos(-t / T * 2 * Math.PI) - gw / 2),
                        Math.floor(center[1] + (R - r) * rx * settings.ellipticity * Math.sin(-t / T * 2 * Math.PI) - gh / 2),
                        t / T * 2 * Math.PI
                    ]);
                }

                if (shuffle(points).some(drawAt)) {
                    return true;
                }
            }

            return false;
        };

        // Setup
        ngx = Math.floor(canvas.getAttribute('width') / g);
        ngy = Math.floor(canvas.getAttribute('height') / g);
        ctx = canvas.getContext('2d');
        grid = [];

        var bctx = document.createElement('canvas').getContext('2d');

        bctx.fillStyle = settings.backgroundColor;
        bctx.clearRect(0, 0, 1, 1);
        bctx.fillRect(0, 0, 1, 1);
        bgPixel = bctx.getImageData(0, 0, 1, 1).data;

        if (typeof settings.wordColor !== 'function' && settings.wordColor.substr(0, 6) !== 'random') {
            bctx.fillStyle = settings.wordColor;
            bctx.fillRect(0, 0, 1, 1);

            var wdPixel = bctx.getImageData(0, 0, 1, 1).data,
                c = 4;

            while (c--) {
                if (Math.abs(wdPixel[c] - bgPixel[c]) > 10) {
                    diffChannel = c;
                    break;
                }
            }
        } else {
            diffChannel = NaN;
        }

        var gx = ngx,
            gy;

        while (gx--) {
            grid[gx] = [];
            gy = ngy;

            while (gy--) {
                grid[gx][gy] = true;
            }
        }

        if (settings.clearCanvas) {
            ctx.fillStyle = settings.backgroundColor;
            ctx.clearRect(0, 0, ngx * (g + 1), ngy * (g + 1));
            ctx.fillRect(0, 0, ngx * (g + 1), ngy * (g + 1));
        } else {
            updateGrid(0, 0, ngx, ngy, true);
        }

        ctx.textBaseline = 'top';

        // Stop any cloud that is still being drawn on this canvas
        trigger(canvas, 'wordcloudstart');

        var i = 0;

        if (settings.wait !== 0) {
            var timer = window.setInterval(function () {
                if (i >= settings.wordList.length) {
                    window.clearInterval(timer);
                    trigger(canvas, 'wordcloudstop');
                    return;
                }

                escapeTime = (new Date()).getTime();
                putWord(settings.wordList[i][0], settings.wordList[i][1]);

                if (exceedTime()) {
                    window.clearInterval(timer);
                    settings.abort();
                    trigger(canvas, 'wordcloudabort');
                    trigger(canvas, 'wordcloudstop');
                }

                i++;
            }, settings.wait);

            once(canvas, 'wordcloudstart', function () {
                window.clearInterval(timer);
            });
        } else {
            var stop = false;

            setImmediate(function loop() {
                if (stop) {
                    return;
                }

                if (i >= settings.wordList.length) {
                    trigger(canvas, 'wordcloudstop');
                    return;
                }

                escapeTime = (new Date()).getTime();
                putWord(settings.wordList[i][0], settings.wordList[i][1]);

                if (exceedTime()) {
                    settings.abort();
                    trigger(canvas, 'wordcloudabort');
                    trigger(canvas, 'wordcloudstop');
                    return;
                }

                i++;
                setImmediate(loop);
            });

            once(canvas, 'wordcloudstart', function () {
                stop = true;
            });
        }
    }

    WordCloud.isSupported = isSupported;
    WordCloud.minimumFontSize = minimumFontSize;

    window.WordCloud = WordCloud;
}(window, document));
