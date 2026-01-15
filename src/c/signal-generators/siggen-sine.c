#include <stdio.h>
#include <math.h>
#include <jack/jack.h>


jack_client_t *client;
jack_port_t *output_port;
double phase = 0.0;
double freq = 440.0; // Standard A4 tuning

// The process callback: This is called by JACK every buffer cycle
int process(jack_nframes_t nframes, void *arg) {
    float *out = (float *)jack_port_get_buffer(output_port, nframes);
    double sr = (double)jack_get_sample_rate(client);
    
    for (jack_nframes_t i = 0; i < nframes; i++) {
        out[i] = (float)(0.5 * sin(phase)); // 0.5 amplitude to avoid clipping
        phase += 2.0 * M_PI * freq / sr;
        
        // Keep phase within 0 -> 2pi to prevent precision loss over time
        if (phase >= 2.0 * M_PI) phase -= 2.0 * M_PI;
    }
    return 0;
}

int main() {
    // 1. Open the client
    client = jack_client_open("SineGen", JackNullOption, NULL);
    if (!client) {
        fprintf(stderr, "JACK server not running?\n");
        return 1;
    }

    // 2. Register one output port
    output_port = jack_port_register(client, "output", JACK_DEFAULT_AUDIO_TYPE, JackPortIsOutput, 0);

    // 3. Tell JACK to use our process function
    jack_set_process_callback(client, process, NULL);

    // 4. Activate the client
    if (jack_activate(client)) {
        fprintf(stderr, "Could not activate client\n");
        return 1;
    }

    printf("Sine generator running at %.0fHz. Press Enter to quit...\n", freq);
    getchar();

    jack_client_close(client);
    return 0;
}

	
int zzmain() {
    printf("hi");
}